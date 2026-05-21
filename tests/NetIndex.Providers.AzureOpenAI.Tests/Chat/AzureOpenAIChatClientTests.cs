using Azure;
using FluentAssertions;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.AzureOpenAI.Tests.TestSupport;
using NSubstitute;
using OpenAI.Chat;
using Xunit;

namespace NetIndex.Providers.AzureOpenAI.Tests.Chat;

public sealed class AzureOpenAIChatClientTests
{
    [Fact]
    public async Task GenerateStreamingAsync_AggregatesContentPartsAsync()
    {
        var chatClient = Substitute.For<ChatClient>();
        chatClient.CompleteChatStreamingAsync(Arg.Any<IEnumerable<ChatMessage>>(), null, Arg.Any<CancellationToken>())
            .Returns(new TestAsyncCollectionResult<StreamingChatCompletionUpdate>([
                Update("Hello", null),
                Update(" world", ChatFinishReason.Stop),
            ]));
        await using var client = new AzureOpenAIChatClient(chatClient);

        var chunks = await CollectAsync(client.GenerateStreamingAsync("prompt", []));

        chunks.Select(c => c.Text).Should().Equal("Hello", " world");
        chunks[^1].IsComplete.Should().BeTrue();
        chunks[^1].FinishReason.Should().Be(FinishReason.Stop);
    }

    [Fact]
    public async Task GenerateAsync_ReturnsConcatenatedTextAsync()
    {
        var chatClient = Substitute.For<ChatClient>();
        chatClient.CompleteChatStreamingAsync(Arg.Any<IEnumerable<ChatMessage>>(), null, Arg.Any<CancellationToken>())
            .Returns(new TestAsyncCollectionResult<StreamingChatCompletionUpdate>([
                Update("Hello", null),
                Update(" world", ChatFinishReason.Stop),
            ]));
        await using var client = new AzureOpenAIChatClient(chatClient);

        var result = await client.GenerateAsync("prompt", []);

        result.Should().Be("Hello world");
    }

    [Fact]
    public async Task GenerateStreamingAsync_EmptyStream_ThrowsEmptyResponseAsync()
    {
        var chatClient = Substitute.For<ChatClient>();
        chatClient.CompleteChatStreamingAsync(Arg.Any<IEnumerable<ChatMessage>>(), null, Arg.Any<CancellationToken>())
            .Returns(new TestAsyncCollectionResult<StreamingChatCompletionUpdate>([Update("", ChatFinishReason.Stop)]));
        await using var client = new AzureOpenAIChatClient(chatClient);

        var act = () => CollectAsync(client.GenerateStreamingAsync("prompt", []));

        var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
        ex.Which.ErrorCode.Should().Be("empty_response");
        ex.Which.IsRetryable.Should().BeFalse();
    }

    [Theory]
    [InlineData(ChatFinishReason.Stop, FinishReason.Stop)]
    [InlineData(ChatFinishReason.Length, FinishReason.Length)]
    [InlineData(ChatFinishReason.ContentFilter, FinishReason.ContentFilter)]
    [InlineData(ChatFinishReason.ToolCalls, FinishReason.Stop)]
    [InlineData(ChatFinishReason.FunctionCall, FinishReason.Stop)]
    public async Task GenerateStreamingAsync_FinishReason_MappedExhaustivelyAsync(ChatFinishReason upstream, FinishReason expected)
    {
        var chatClient = Substitute.For<ChatClient>();
        chatClient.CompleteChatStreamingAsync(Arg.Any<IEnumerable<ChatMessage>>(), null, Arg.Any<CancellationToken>())
            .Returns(new TestAsyncCollectionResult<StreamingChatCompletionUpdate>([Update("done", upstream)]));
        await using var client = new AzureOpenAIChatClient(chatClient);

        var chunks = await CollectAsync(client.GenerateStreamingAsync("prompt", []));

        chunks.Single().FinishReason.Should().Be(expected);
    }

    [Fact]
    public async Task GenerateStreamingAsync_CancellationFromCallerToken_RethrowsAsync()
    {
        using var cts = new CancellationTokenSource();
        var chatClient = Substitute.For<ChatClient>();
        chatClient.CompleteChatStreamingAsync(Arg.Any<IEnumerable<ChatMessage>>(), null, cts.Token)
            .Returns(_ => throw new OperationCanceledException(cts.Token));
        await using var client = new AzureOpenAIChatClient(chatClient);

        await cts.CancelAsync();
        var act = () => CollectAsync(client.GenerateStreamingAsync("prompt", [], cts.Token));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GenerateStreamingAsync_MidStreamProviderException_WrapsAsync()
    {
        var chatClient = Substitute.For<ChatClient>();
        chatClient.CompleteChatStreamingAsync(Arg.Any<IEnumerable<ChatMessage>>(), null, Arg.Any<CancellationToken>())
            .Returns(new TestAsyncCollectionResult<StreamingChatCompletionUpdate>(
                [Update("partial", null)],
                new RequestFailedException(503, "service unavailable")));
        await using var client = new AzureOpenAIChatClient(chatClient);

        var act = () => CollectAsync(client.GenerateStreamingAsync("prompt", []));

        var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
        ex.Which.ProviderName.Should().Be("AzureOpenAI");
        ex.Which.ErrorCode.Should().Be("http_503");
        ex.Which.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateStreamingAsync_DisposeMidEnumeration_ThrowsObjectDisposedExceptionAsync()
    {
        var chatClient = Substitute.For<ChatClient>();
        chatClient.CompleteChatStreamingAsync(Arg.Any<IEnumerable<ChatMessage>>(), null, Arg.Any<CancellationToken>())
            .Returns(new TestAsyncCollectionResult<StreamingChatCompletionUpdate>([
                Update("first", null),
                Update("second", ChatFinishReason.Stop),
            ]));
        var client = new AzureOpenAIChatClient(chatClient);
        await using var enumerator = client.GenerateStreamingAsync("prompt", []).GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue();

        await client.DisposeAsync();
        var act = async () => await enumerator.MoveNextAsync();

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Theory]
    [InlineData("GenerateAsync")]
    [InlineData("GenerateStreamingAsync")]
    public async Task PublicMethods_AfterDispose_ThrowObjectDisposedExceptionAsync(string method)
    {
        var chatClient = Substitute.For<ChatClient>();
        var client = new AzureOpenAIChatClient(chatClient);
        await client.DisposeAsync();

        Func<Task> act = method switch
        {
            "GenerateAsync" => () => client.GenerateAsync("prompt", []),
            "GenerateStreamingAsync" => () => CollectAsync(client.GenerateStreamingAsync("prompt", [])),
            _ => throw new InvalidOperationException(),
        };

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task DisposeAsync_Idempotent_AfterDoubleCallAsync()
    {
        var chatClient = Substitute.For<ChatClient>();
        var client = new AzureOpenAIChatClient(chatClient);

        await client.DisposeAsync();
        await client.DisposeAsync();

        true.Should().BeTrue();
    }

    // Patch #10: unknown finish reason should fallback to Stop.
    [Fact]
    public async Task GenerateStreamingAsync_UnknownFinishReason_FallbackToStopAsync()
    {
        var chatClient = Substitute.For<ChatClient>();
        chatClient.CompleteChatStreamingAsync(Arg.Any<IEnumerable<ChatMessage>>(), null, Arg.Any<CancellationToken>())
            .Returns(new TestAsyncCollectionResult<StreamingChatCompletionUpdate>([
                Update("text", (ChatFinishReason?)null),
            ]));
        await using var client = new AzureOpenAIChatClient(chatClient);

        var chunks = await CollectAsync(client.GenerateStreamingAsync("prompt", []));

        // Should emit a terminal IsComplete=true chunk because no FinishReason was seen.
        chunks.Should().SatisfyRespectively(
            c => { c.IsComplete.Should().BeFalse(); c.Text.Should().Be("text"); },
            c => { c.IsComplete.Should().BeTrue(); c.FinishReason.Should().Be(FinishReason.Stop); });
    }

    private static StreamingChatCompletionUpdate Update(string text, ChatFinishReason? finishReason) =>
        OpenAIChatModelFactory.StreamingChatCompletionUpdate(
            completionId: "completion-id",
            contentUpdate: new ChatMessageContent([ChatMessageContentPart.CreateTextPart(text)]),
            functionCallUpdate: null,
            toolCallUpdates: [],
            role: ChatMessageRole.Assistant,
            refusalUpdate: null,
            contentTokenLogProbabilities: [],
            refusalTokenLogProbabilities: [],
            finishReason: finishReason,
            createdAt: DateTimeOffset.UtcNow,
            model: "gpt-4o-mini",
            systemFingerprint: "fingerprint",
            usage: null);

    private static async Task<List<GenerationChunk>> CollectAsync(IAsyncEnumerable<GenerationChunk> stream)
    {
        var result = new List<GenerationChunk>();
        await foreach (var chunk in stream)
        {
            result.Add(chunk);
        }
        return result;
    }
}
