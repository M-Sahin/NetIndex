using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Core.Abstractions.Telemetry;
using NetIndex.Providers.TextEmbeddingsInference.Options;

namespace NetIndex.Providers.TextEmbeddingsInference;

/// <summary>
/// Reranks search results with a cross-encoder served by Text Embeddings Inference (<c>POST /rerank</c>).
/// </summary>
public sealed class TeiDocumentReranker : IDocumentReranker, IDisposable
{
    internal const string ProviderName = "TextEmbeddingsInference";

    private readonly HttpClient _httpClient;
    private readonly Uri _rerankUri;
    private bool _disposed;

    /// <summary>Initializes with the configured options.</summary>
    /// <param name="options">Resolved reranker options.</param>
    public TeiDocumentReranker(IOptions<TeiRerankerOptions> options)
        : this(CreateClient(options), options.Value, TimeProvider.System)
    {
    }

    internal TeiDocumentReranker(HttpClient httpClient, TeiRerankerOptions options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        _httpClient = httpClient;
        _rerankUri = new Uri(new Uri(options.Endpoint.TrimEnd('/') + "/", UriKind.Absolute), "rerank");
        Breaker = new TeiCircuitBreaker(options.FailureThreshold, options.BreakDuration, clock);
    }

    internal TeiCircuitBreaker Breaker { get; }

    private static HttpClient CreateClient(IOptions<TeiRerankerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opt = options.Value;
        return new HttpClient(CreateHandler(opt), disposeHandler: true) { Timeout = opt.RequestTimeout };
    }

    internal static SocketsHttpHandler CreateHandler(TeiRerankerOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = options.ConnectTimeout,

            // A 3xx from the sidecar must never replay the question and chunk text to another host.
            AllowAutoRedirect = false,
        };
        if (options.RestrictToPrivateNetwork)
        {
            handler.ConnectCallback = (context, cancellationToken) =>
                TeiPrivateNetworkGuard.ConnectAsync(context.DnsEndPoint, TeiPrivateNetworkGuard.ResolveAsync, cancellationToken);
        }

        return handler;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<SearchResult<RagChunk>>> RerankAsync(
        IEnumerable<SearchResult<RagChunk>> results,
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(query);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var input = results.ToList();
        if (input.Count <= 1)
        {
            return input;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!Breaker.TryAcquire(out var admission))
        {
            throw new TeiCircuitOpenException(
                "The Text Embeddings Inference reranker circuit is open; no request was attempted.");
        }

        using var activity = NetIndexActivitySource.Source.StartActivity("Tei.Rerank");

        // Every admitted call ends in exactly one of RecordSuccess, RecordFailure or Release.
        var settled = false;
        try
        {
            var scores = await ScoreAsync(input, query, cancellationToken).ConfigureAwait(false);
            var ordered = Order(input, scores);
            settled = true;
            Breaker.RecordSuccess(admission);
            return ordered;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NetIndexProviderException ex)
        {
            Fail(activity, ex.ErrorCode);

            // A 4xx proves the service is reachable; only outage-type failures count towards opening the circuit.
            settled = true;
            if (ex.IsRetryable || ex.ErrorCode == "invalid_response")
            {
                Breaker.RecordFailure(admission);
            }
            else
            {
                Breaker.RecordSuccess(admission);
            }

            throw;
        }
        catch (Exception ex)
        {
            Fail(activity, "unexpected_failure");
            settled = true;
            Breaker.RecordFailure(admission);
            throw new NetIndexProviderException(
                "The Text Embeddings Inference reranker failed unexpectedly.",
                isRetryable: false, providerName: ProviderName,
                errorCode: "unexpected_failure", httpStatusCode: null, innerException: ex);
        }
        finally
        {
            if (!settled)
            {
                Breaker.Release(admission);
            }
        }
    }

    private static void Fail(Activity? activity, string? errorCode)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, errorCode);
        activity.SetTag("error.type", errorCode);
    }

    private async Task<float[]> ScoreAsync(
        List<SearchResult<RagChunk>> input, string query, CancellationToken cancellationToken)
    {
        var body = new RerankRequest(query, input.Select(r => r.Item.Text).ToArray(), Truncate: true);
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(_rerankUri, body, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw ClassifyStatus((int)response.StatusCode);
            }

            var items = await response.Content
                .ReadFromJsonAsync<List<RerankItem?>>(cancellationToken)
                .ConfigureAwait(false);
            return MapScores(items, input.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new NetIndexProviderException(
                "The Text Embeddings Inference reranker timed out.",
                isRetryable: true, providerName: ProviderName,
                errorCode: "timeout", httpStatusCode: null, innerException: ex);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw InvalidResponse(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException)
        {
            var refused = HasInner<TeiNonPrivateAddressException>(ex);
            throw new NetIndexProviderException(
                refused
                    ? "The Text Embeddings Inference reranker host resolved to a non-private address; no request was sent."
                    : "Unable to connect to the Text Embeddings Inference reranker.",
                isRetryable: true, providerName: ProviderName,
                errorCode: refused ? "non_private_address" : "connection_failed", httpStatusCode: null, innerException: ex);
        }
    }

    private static bool HasInner<T>(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is T)
            {
                return true;
            }
        }

        return false;
    }

    private static NetIndexProviderException InvalidResponse(Exception? inner)
        => new(
            "The Text Embeddings Inference reranker returned an unusable response.",
            isRetryable: false, providerName: ProviderName,
            errorCode: "invalid_response", httpStatusCode: null, innerException: inner);

    private static NetIndexProviderException ClassifyStatus(int code)
    {
        if (code == 429)
        {
            return new NetIndexProviderException(
                "The Text Embeddings Inference reranker rate-limited the request (HTTP 429).",
                isRetryable: true, providerName: ProviderName,
                errorCode: "rate_limited", httpStatusCode: 429, innerException: null);
        }
        return new NetIndexProviderException(
            $"The Text Embeddings Inference reranker returned HTTP {code}.",
            isRetryable: code >= 500 || code == 408, providerName: ProviderName,
            errorCode: $"http_{code}", httpStatusCode: code, innerException: null);
    }

    private static float[] MapScores(List<RerankItem?>? items, int expected)
    {
        if (items is null || items.Count != expected)
        {
            throw InvalidResponse(null);
        }
        var scores = new float[expected];
        var seen = new bool[expected];
        foreach (var item in items)
        {
            if (item is null || item.Index < 0 || item.Index >= expected || seen[item.Index] || !float.IsFinite(item.Score))
            {
                throw InvalidResponse(null);
            }
            seen[item.Index] = true;
            scores[item.Index] = item.Score;
        }
        return scores;
    }

    private static List<SearchResult<RagChunk>> Order(List<SearchResult<RagChunk>> input, float[] scores)
        => input
            .Select((r, i) => (Result: r with { Score = scores[i] }, Index: i))
            .OrderByDescending(t => t.Result.Score)
            .ThenBy(t => t.Index)
            .Select(t => t.Result)
            .ToList();

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _httpClient.Dispose();
    }

    private sealed record RerankRequest(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("texts")] string[] Texts,
        [property: JsonPropertyName("truncate")] bool Truncate);

    private sealed record RerankItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("score")] float Score);
}
