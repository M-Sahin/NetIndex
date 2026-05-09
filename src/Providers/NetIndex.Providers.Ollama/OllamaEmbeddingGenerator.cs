using System.Net.Sockets;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.Ollama.Options;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Exceptions;

namespace NetIndex.Providers.Ollama;

/// <summary>Generates embeddings using the Ollama API via OllamaSharp.</summary>
public sealed class OllamaEmbeddingGenerator : IEmbeddingGenerator, IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly OllamaApiClient _client;
    private readonly string _model;
    private readonly int _dimensions;
    private bool _disposed;

    /// <inheritdoc />
    public int Dimensions => _dimensions;

    /// <summary>Initializes with the configured options.</summary>
    /// <param name="options">Resolved Ollama options.</param>
    public OllamaEmbeddingGenerator(IOptions<OllamaOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opt = options.Value;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(opt.Endpoint),
            Timeout = opt.Timeout,
        };
        _model = opt.Model;
        _client = new OllamaApiClient(_httpClient) { SelectedModel = opt.Model };
        _dimensions = opt.Dimensions;
    }

    internal OllamaEmbeddingGenerator(HttpClient httpClient, string model, int dimensions)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(model);
        _httpClient = httpClient;
        _model = model;
        _client = new OllamaApiClient(httpClient) { SelectedModel = model };
        _dimensions = dimensions;
    }

    /// <inheritdoc />
    public async Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            var request = new EmbedRequest { Model = _model, Input = new List<string> { text } };
            var response = await _client.EmbedAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.Embeddings is null || response.Embeddings.Count == 0)
            {
                throw new NetIndexProviderException(
                    "Ollama returned an empty embeddings response.",
                    isRetryable: false, providerName: "Ollama",
                    errorCode: "empty_response", httpStatusCode: null, innerException: null);
            }
            return response.Embeddings[0];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue && (int)ex.StatusCode.Value == 429)
        {
            // HTTP 429 is retryable per NetIndexProviderException framework contract.
            throw new NetIndexProviderException(
                $"Ollama rate-limited (HTTP 429): {ex.Message}",
                isRetryable: true, providerName: "Ollama",
                errorCode: "rate_limited", httpStatusCode: 429, innerException: ex);
        }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue && (int)ex.StatusCode.Value >= 500)
        {
            var code = (int)ex.StatusCode.Value;
            throw new NetIndexProviderException(
                $"Ollama returned HTTP {code}: {ex.Message}",
                isRetryable: true, providerName: "Ollama",
                errorCode: $"http_{code}", httpStatusCode: code, innerException: ex);
        }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue)
        {
            var code = (int)ex.StatusCode.Value;
            throw new NetIndexProviderException(
                $"Ollama returned HTTP {code}: {ex.Message}",
                isRetryable: false, providerName: "Ollama",
                errorCode: $"http_{code}", httpStatusCode: code, innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new OllamaConnectionException(
                "Unable to connect to Ollama. Ensure the Ollama service is running.", ex);
        }
        catch (SocketException ex)
        {
            throw new OllamaConnectionException(
                "Unable to connect to Ollama. Ensure the Ollama service is running.", ex);
        }
        catch (IOException ex)
        {
            throw new OllamaConnectionException(
                "Unable to connect to Ollama. Ensure the Ollama service is running.", ex);
        }
        catch (OllamaException ex)
        {
            // OllamaSharp 5.x wraps HTTP errors in OllamaException; recover status code if available
            if (ex.InnerException is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
            {
                var code = (int)httpEx.StatusCode.Value;
                var retryable = code >= 500 || code == 429;
                throw new NetIndexProviderException(
                    $"Ollama API error (HTTP {code}): {ex.Message}",
                    isRetryable: retryable, providerName: "Ollama",
                    errorCode: $"http_{code}", httpStatusCode: code, innerException: ex);
            }
            throw new NetIndexProviderException(
                $"Ollama API error: {ex.Message}",
                isRetryable: false, providerName: "Ollama",
                errorCode: "api_error", httpStatusCode: null, innerException: ex);
        }
    }

    /// <inheritdoc />
    public async Task<float[][]> GenerateBatchAsync(
        IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var results = new List<float[]>();
        foreach (var text in texts)
        {
            results.Add(await GenerateAsync(text, cancellationToken).ConfigureAwait(false));
        }
        return results.ToArray();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
