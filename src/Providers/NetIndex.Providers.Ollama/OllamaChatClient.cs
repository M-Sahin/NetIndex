using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.Ollama.Options;
using OllamaSharp;
using OllamaSharp.Models.Chat;
using OllamaSharp.Models.Exceptions;

namespace NetIndex.Providers.Ollama;

/// <summary>
/// Streams chat completions from a local Ollama instance via OllamaSharp.
/// </summary>
public sealed class OllamaChatClient : IChatClient, IAsyncDisposable
{
    private const string SystemPromptInstruction =
        "You are a helpful assistant. Use the provided context to answer the user's question accurately. " +
        "If the answer is not contained in the context, say so.";

    private readonly HttpClient _httpClient;
    private readonly OllamaApiClient _client;
    private readonly string _model;
    private bool _disposed;

    /// <summary>Initializes the chat client from resolved options.</summary>
    /// <param name="options">Resolved Ollama chat options.</param>
    public OllamaChatClient(IOptions<OllamaChatOptions> options)
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
    }

    internal OllamaChatClient(HttpClient httpClient, string model)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(model);
        _httpClient = httpClient;
        _model = model;
        _client = new OllamaApiClient(httpClient) { SelectedModel = model };
    }

    /// <inheritdoc />
    public async Task<string> GenerateAsync(
        string prompt,
        IEnumerable<RagChunk> context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var buffer = new System.Text.StringBuilder();
        await foreach (var chunk in GenerateStreamingAsync(prompt, context, cancellationToken)
                       .ConfigureAwait(false))
        {
            buffer.Append(chunk.Text);
        }

        if (buffer.Length == 0)
        {
            throw new NetIndexProviderException(
                "Ollama returned an empty chat response.",
                isRetryable: false, providerName: "Ollama",
                errorCode: "empty_response", httpStatusCode: null, innerException: null);
        }

        return buffer.ToString();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<GenerationChunk> GenerateStreamingAsync(
        string prompt,
        IEnumerable<RagChunk> context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var request = BuildRequest(prompt, context);
        await foreach (var generation in SafeChatAsync(request, cancellationToken).ConfigureAwait(false))
        {
            yield return generation;
        }
    }

    private ChatRequest BuildRequest(string prompt, IEnumerable<RagChunk> context)
    {
        var contextBlock = string.Join(
            "\n---\n",
            context.Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Text))
                   .Select(c => c.Text));

        var userContent = string.IsNullOrEmpty(contextBlock)
            ? prompt
            : $"Context:\n{contextBlock}\n\nQuestion: {prompt}";

        return new ChatRequest
        {
            Model = _model,
            Stream = true,
            Messages =
            [
                new Message { Role = ChatRole.System, Content = SystemPromptInstruction },
                new Message { Role = ChatRole.User, Content = userContent },
            ],
        };
    }

    private async IAsyncEnumerable<GenerationChunk> SafeChatAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerator<ChatResponseStream?>? enumerator = null;
        try
        {
            enumerator = _client.ChatAsync(request, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception ex) when (TryWrap(ex, out var wrapped))
        {
            throw wrapped;
        }

        try
        {
            while (true)
            {
                bool moved;
                try
                {
                    moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (TryWrap(ex, out var wrapped))
                {
                    throw wrapped;
                }
                if (!moved)
                {
                    yield break;
                }

                var response = enumerator.Current;
                if (response is null)
                {
                    continue;
                }

                var text = response.Message?.Content ?? string.Empty;
                var isComplete = response.Done;
                var finishReason = isComplete
                    ? MapDoneReason((response as ChatDoneResponseStream)?.DoneReason)
                    : FinishReason.Stop;

                yield return new GenerationChunk(text, isComplete, finishReason);
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static FinishReason MapDoneReason(string? doneReason) =>
        doneReason switch
        {
            "stop" => FinishReason.Stop,
            "length" => FinishReason.Length,
            _ => FinishReason.Stop,
        };

    private static bool TryWrap(Exception ex, out Exception wrapped)
    {
        switch (ex)
        {
            case OperationCanceledException:
                wrapped = ex;
                return false;
            case HttpRequestException http when http.StatusCode.HasValue && (int)http.StatusCode.Value == 429:
                wrapped = new NetIndexProviderException(
                    $"Ollama rate-limited (HTTP 429): {http.Message}",
                    isRetryable: true, providerName: "Ollama",
                    errorCode: "rate_limited", httpStatusCode: 429, innerException: http);
                return true;
            case HttpRequestException http when http.StatusCode.HasValue && (int)http.StatusCode.Value >= 500:
                {
                    var code = (int)http.StatusCode.Value;
                    wrapped = new NetIndexProviderException(
                        $"Ollama returned HTTP {code}: {http.Message}",
                        isRetryable: true, providerName: "Ollama",
                        errorCode: $"http_{code}", httpStatusCode: code, innerException: http);
                    return true;
                }
            case HttpRequestException http when http.StatusCode.HasValue:
                {
                    var code = (int)http.StatusCode.Value;
                    wrapped = new NetIndexProviderException(
                        $"Ollama returned HTTP {code}: {http.Message}",
                        isRetryable: false, providerName: "Ollama",
                        errorCode: $"http_{code}", httpStatusCode: code, innerException: http);
                    return true;
                }
            case HttpRequestException:
            case SocketException:
            case IOException:
                wrapped = new OllamaConnectionException(
                    "Unable to connect to Ollama. Ensure the Ollama service is running.", ex);
                return true;
            case OllamaException oex when oex.InnerException is HttpRequestException httpEx && httpEx.StatusCode.HasValue:
                {
                    var code = (int)httpEx.StatusCode.Value;
                    var retryable = code >= 500 || code == 429;
                    wrapped = new NetIndexProviderException(
                        $"Ollama API error (HTTP {code}): {oex.Message}",
                        isRetryable: retryable, providerName: "Ollama",
                        errorCode: $"http_{code}", httpStatusCode: code, innerException: oex);
                    return true;
                }
            case OllamaException:
                wrapped = new NetIndexProviderException(
                    $"Ollama API error: {ex.Message}",
                    isRetryable: false, providerName: "Ollama",
                    errorCode: "api_error", httpStatusCode: null, innerException: ex);
                return true;
            default:
                wrapped = ex;
                return false;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
