using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Core.Abstractions.Telemetry;
using NetIndex.Providers.OpenAI.Options;
using OpenAI;
using OpenAI.Chat;

namespace NetIndex.Providers.OpenAI;

/// <summary>
/// Streams chat completions from the standard OpenAI API via the official .NET SDK.
/// </summary>
/// <remarks>
/// The provider lets the OpenAI SDK own its HTTP pipeline and configures request timeout through
/// <c>OpenAIClientOptions.NetworkTimeout</c> instead of owning a separate <c>HttpClient</c>.
/// </remarks>
public sealed class OpenAIChatClient : IChatClient, IAsyncDisposable
{
    private const string SystemPromptInstruction =
        "You are a helpful assistant. Use the provided context to answer the user's question accurately. " +
        "If the answer is not contained in the context, say so.";

    private readonly OpenAIClient? _openAIClient;
    private readonly ChatClient _chatClient;
    private readonly string _modelName;
    private int _disposeState;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIChatClient"/> class.
    /// </summary>
    /// <param name="options">Resolved OpenAI options.</param>
    public OpenAIChatClient(IOptions<OpenAIOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opt = options.Value;

        _modelName = opt.ChatModel;
        try
        {
            _openAIClient = OpenAIProviderHelpers.CreateClient(opt.ApiKey, opt.Endpoint, opt.Timeout);
            _chatClient = _openAIClient.GetChatClient(opt.ChatModel);
        }
        catch
        {
            DisposeClient(_openAIClient);
            throw;
        }
    }

    internal OpenAIChatClient(ChatClient chatClient)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        _modelName = string.Empty;
    }

    /// <inheritdoc />
    public async Task<string> GenerateAsync(
        string prompt,
        IEnumerable<RagChunk> context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(_disposeState != 0, this);

        using var activity = NetIndexActivitySource.Source.StartActivity("OpenAI.GenerateChat");
        activity?.SetTag("openai.model", _modelName);
        var buffer = new StringBuilder();
        await foreach (var chunk in GenerateStreamingAsync(prompt, context, cancellationToken).ConfigureAwait(false))
        {
            buffer.Append(chunk.Text);
        }

        if (buffer.Length == 0)
        {
            throw new NetIndexProviderException(
                "OpenAI returned an empty chat response.",
                isRetryable: false,
                providerName: OpenAIProviderHelpers.ProviderName,
                errorCode: "empty_response",
                httpStatusCode: null,
                innerException: null);
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
        ObjectDisposedException.ThrowIf(_disposeState != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        using var activity = NetIndexActivitySource.Source.StartActivity("OpenAI.GenerateChatStreaming");
        activity?.SetTag("openai.model", _modelName);

        var messages = BuildMessages(prompt, context);
        IAsyncEnumerator<StreamingChatCompletionUpdate>? enumerator = null;
        try
        {
            enumerator = _chatClient.CompleteChatStreamingAsync(
                messages,
                options: null,
                cancellationToken).GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception ex)
        {
            var wrapped = OpenAIProviderHelpers.Wrap(ex, cancellationToken);
            if (OpenAIProviderHelpers.ShouldRethrowOriginal(ex, wrapped))
            {
                throw;
            }
            throw OpenAIProviderHelpers.ThrowPreserveContext(wrapped);
        }

        try
        {
            var emittedText = false;
            var sawFinish = false;
            var moved = false;
            while (true)
            {
                try
                {
                    moved = await enumerator!.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var wrapped = OpenAIProviderHelpers.Wrap(ex, cancellationToken);
                    if (OpenAIProviderHelpers.ShouldRethrowOriginal(ex, wrapped))
                    {
                        throw;
                    }
                    throw OpenAIProviderHelpers.ThrowPreserveContext(wrapped);
                }

                if (!moved)
                {
                    if (!emittedText)
                    {
                        throw new NetIndexProviderException(
                            "OpenAI returned an empty chat response.",
                            isRetryable: false,
                            providerName: OpenAIProviderHelpers.ProviderName,
                            errorCode: "empty_response",
                            httpStatusCode: null,
                            innerException: null);
                    }

                    if (!sawFinish)
                    {
                        yield return new GenerationChunk(string.Empty, true, FinishReason.Stop);
                    }

                    break;
                }

                ObjectDisposedException.ThrowIf(_disposeState != 0, this);
                var update = enumerator!.Current;
                var text = BuildText(update);
                var isComplete = update.FinishReason.HasValue;
                if (isComplete)
                {
                    sawFinish = true;
                    // Reject an all-empty response before yielding a terminal success chunk so
                    // callers never see a terminal chunk followed immediately by an exception.
                    if (!emittedText && text.Length == 0)
                    {
                        throw new NetIndexProviderException(
                            "OpenAI returned an empty chat response.",
                            isRetryable: false,
                            providerName: OpenAIProviderHelpers.ProviderName,
                            errorCode: "empty_response",
                            httpStatusCode: null,
                            innerException: null);
                    }
                }
                emittedText = emittedText || text.Length > 0;
                var finishReason = isComplete ? MapFinishReason(update.FinishReason!.Value) : FinishReason.Stop;
                yield return new GenerationChunk(text, isComplete, finishReason);
            }
        }
        finally
        {
            if (enumerator is not null)
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }
        return DisposeClientsAsync();
    }

    private static IReadOnlyList<ChatMessage> BuildMessages(string prompt, IEnumerable<RagChunk> context)
    {
        var contextBlock = string.Join(
            "\n---\n",
            context.Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Text)).Select(c => c.Text));
        var userContent = string.IsNullOrEmpty(contextBlock)
            ? prompt
            : $"Context:\n{contextBlock}\n\nQuestion: {prompt}";
        return
        [
            new SystemChatMessage(SystemPromptInstruction),
            new UserChatMessage(userContent),
        ];
    }

    private static string BuildText(StreamingChatCompletionUpdate update)
    {
        var buffer = new StringBuilder();
        foreach (var part in update.ContentUpdate)
        {
            buffer.Append(part.Text);
        }
        return buffer.ToString();
    }

    private static FinishReason MapFinishReason(ChatFinishReason finishReason) => finishReason switch
    {
        ChatFinishReason.Stop => FinishReason.Stop,
        ChatFinishReason.Length => FinishReason.Length,
        ChatFinishReason.ContentFilter => FinishReason.ContentFilter,
        ChatFinishReason.ToolCalls => FinishReason.Stop,
        ChatFinishReason.FunctionCall => FinishReason.Stop,
        _ => FinishReason.Stop,
    };

    private async ValueTask DisposeClientsAsync()
    {
        try
        {
            await DisposeClientAsync(_chatClient).ConfigureAwait(false);
        }
        finally
        {
            await DisposeClientAsync(_openAIClient).ConfigureAwait(false);
        }
    }

    private static void DisposeClient(object? client)
    {
        if (client is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static async ValueTask DisposeClientAsync(object? client)
    {
        switch (client)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
}
