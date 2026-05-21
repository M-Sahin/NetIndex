using System.Runtime.CompilerServices;
using System.Text;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Core.Abstractions.Telemetry;
using NetIndex.Providers.AzureOpenAI.Options;
using OpenAI.Chat;

namespace NetIndex.Providers.AzureOpenAI;

/// <summary>
/// Streams chat completions from Azure OpenAI via the Azure SDK.
/// </summary>
/// <remarks>
/// The provider lets the Azure SDK own its HTTP pipeline and configures request timeout through
/// <c>AzureOpenAIClientOptions.NetworkTimeout</c> instead of owning a separate <c>HttpClient</c>.
/// </remarks>
public sealed class AzureOpenAIChatClient : IChatClient, IAsyncDisposable
{
    private const string SystemPromptInstruction =
        "You are a helpful assistant. Use the provided context to answer the user's question accurately. " +
        "If the answer is not contained in the context, say so.";

    private readonly AzureOpenAIClient? _azureClient;
    private readonly ChatClient _chatClient;
    private readonly string? _deploymentName;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureOpenAIChatClient"/> class.
    /// </summary>
    /// <param name="options">Resolved Azure OpenAI chat options.</param>
    public AzureOpenAIChatClient(IOptions<AzureOpenAIChatOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opt = options.Value;
        ArgumentNullException.ThrowIfNull(opt.Endpoint);

        _deploymentName = opt.ChatDeployment;
        try
        {
            _azureClient = AzureOpenAIProviderHelpers.CreateClient(
                opt.Endpoint,
                opt.ApiKey,
                opt.Credential,
                opt.ApiVersion,
                opt.Timeout);
            _chatClient = _azureClient.GetChatClient(opt.ChatDeployment);
        }
        catch
        {
            DisposeClient(_azureClient);
            throw;
        }
    }

    internal AzureOpenAIChatClient(ChatClient chatClient)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
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

        using var activity = NetIndexActivitySource.Source.StartActivity("AzureOpenAI.GenerateChat");
        activity?.SetTag("azure.openai.deployment", _deploymentName);
        var buffer = new StringBuilder();
        await foreach (var chunk in GenerateStreamingAsync(prompt, context, cancellationToken).ConfigureAwait(false))
        {
            buffer.Append(chunk.Text);
        }

        if (buffer.Length == 0)
        {
            throw new NetIndexProviderException(
                "Azure OpenAI returned an empty chat response.",
                isRetryable: false,
                providerName: AzureOpenAIProviderHelpers.ProviderName,
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
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        using var activity = NetIndexActivitySource.Source.StartActivity("AzureOpenAI.GenerateChatStreaming");
        activity?.SetTag("azure.openai.deployment", _deploymentName);

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
            throw AzureOpenAIProviderHelpers.Wrap(ex, cancellationToken);
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
                    throw AzureOpenAIProviderHelpers.Wrap(ex, cancellationToken);
                }

                if (!moved)
                {
                    // Patch #3: reject empty response before any terminal yield.
                    if (!emittedText)
                    {
                        throw new NetIndexProviderException(
                            "Azure OpenAI returned an empty chat response.",
                            isRetryable: false,
                            providerName: AzureOpenAIProviderHelpers.ProviderName,
                            errorCode: "empty_response",
                            httpStatusCode: null,
                            innerException: null);
                    }

                    // Patch #4: if no FinishReason was ever emitted, send a terminal IsComplete=true chunk.
                    if (!sawFinish)
                    {
                        yield return new GenerationChunk(string.Empty, true, FinishReason.Stop);
                    }

                    break;
                }

                ObjectDisposedException.ThrowIf(_disposed, this);
                var update = enumerator!.Current;
                var text = BuildText(update);
                emittedText = emittedText || text.Length > 0;
                var isComplete = update.FinishReason.HasValue;
                if (isComplete)
                {
                    sawFinish = true;
                }
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
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
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
            await DisposeClientAsync(_azureClient).ConfigureAwait(false);
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
