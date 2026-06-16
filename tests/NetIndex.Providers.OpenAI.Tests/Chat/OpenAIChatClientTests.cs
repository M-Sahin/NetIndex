using FluentAssertions;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.OpenAI.Tests.TestSupport;
using NSubstitute;
using OpenAI.Chat;
using Xunit;

namespace NetIndex.Providers.OpenAI.Tests.Chat;

public sealed class OpenAIChatClientTests
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
        await using var client = new OpenAIChatClient(chatClient);

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
        await using var client = new OpenAIChatClient(chatClient);

        var result = await client.GenerateAsync("prompt", []);

        result.Should().Be("Hello world");
    }

    [Fact]
    public async Task GenerateStreamingAsync_EmptyStream_ThrowsEmptyResponseAsync()
    {
        var chatClient = Substitute.For<ChatClient>();
        chatClient.CompleteChatStreamingAsync(Arg.Any<IEnumerable<ChatMessage>>(), null, Arg.Any<CancellationToken>())
            .Returns(new TestAsyncCollectionResult<StreamingChatCompletionUpdate>([Update("", ChatFinishReason.Stop)]));
        await using var client = new OpenAIChatClient(chatClient);

        var act = () => CollectAsync(client.GenerateStreamingAsync("prompt", []));

        var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
        ex.Which.ErrorCode.Should().Be("empty_response");
        ex.Which.IsRetryable.Should().BeFalse();
        ex.Which.ProviderName.Should().Be("OpenAI");
    }

    [Fact]
    public async Task GenerateStreamingAsync_EmptyStreamWithTerminalChunk_ThrowsBeforeYieldingChunkAsync()
    {
        // Verify that the empty_response exception is raised BEFORE any chunk is yielded.
        // Before the fix, a terminal chunk with empty text was yielded and then the exception
        // was thrown on the next MoveNextAsync, giving callers an inconsistent terminal chunk.
        var chatClient = Substitute.For<ChatClient>();
        chatClient.CompleteChatStreamingAsync(Arg.Any<IEnumerable<ChatMessage>>(), null, Arg.Any<CancellationToken>())
            .Returns(new TestAsyncCollectionResult<StreamingChatCompletionUpdate>([Update("", ChatFinishReason.Stop)]));
        await using var client = new OpenAIChatClient(chatClient);

        var yieldedChunks = new List<GenerationChunk>();
        var act = async () =>
        {
            await foreach (var chunk in client.GenerateStreamingAsync("prompt", []))
            {
                yieldedChunks.Add(chunk);
            }
        };

        await act.Should().ThrowAsync<NetIndexProviderException>()
            .Where(ex => ex.ErrorCode == "empty_response");
        yieldedChunks.Should().BeEmpty("no chunk should be yielded before the empty_response exception");
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
        await using var client = new OpenAIChatClient(chatClient);

        var chunks = await CollectAsync(client.GenerateStreamingAsync("prompt", []));

        chunks.Single().FinishReason.Should().Be(expected);
    }

    [Fact]
    public async Task GenerateStreamingAsync_NoFinishReason_EmitsTerminalChunkAsync()
    {
        var chatClient = Substitute.For<ChatClient>();
        chatClient.CompleteChatStreamingAsync(Arg.Any<IEnumerable<ChatMessage>>(), null, Arg.Any<CancellationToken>())
            .Returns(new TestAsyncCollectionResult<StreamingChatCompletionUpdate>([
                Update("text", (ChatFinishReason?)null),
            ]));
        await using var client = new OpenAIChatClient(chatClient);

        var chunks = await CollectAsync(client.GenerateStreamingAsync("prompt", []));

        chunks.Should().SatisfyRespectively(
            c => { c.IsComplete.Should().BeFalse(); c.Text.Should().Be("text"); },
            c => { c.IsComplete.Should().BeTrue(); c.FinishReason.Should().Be(FinishReason.Stop); });
    }

    [Fact]
    public async Task GenerateStreamingAsync_CancellationFromCallerToken_RethrowsAsync()
    {
        using var cts = new CancellationTokenSource();
        var chatClient = Substitute.For<ChatClient>();
        chatClient.CompleteChatStreamingAsync(Arg.Any<IEnumerable<ChatMessage>>(), null, cts.Token)
            .Returns(_ => throw new OperationCanceledException(cts.Token));
        await using var client = new OpenAIChatClient(chatClient);

        await cts.CancelAsync();
        var act = () => CollectAsync(client.GenerateStreamingAsync("prompt", [], cts.Token));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GenerateStreamingAsync_MidStreamProviderException_WrapsAsync()
    {
        var chatClient = Substitute.For<ChatClient>();
        var response = NSubstitute.Substitute.For<System.ClientModel.Primitives.PipelineResponse>();
        response.Status.Returns(503);
        // Pre-create the exception before calling Returns() so NSubstitute doesn't intercept
        // response.Status during Returns() argument evaluation and misassign return types.
        var midStreamException = new System.ClientModel.ClientResultException("service unavailable", response);
        chatClient.CompleteChatStreamingAsync(Arg.Any<IEnumerable<ChatMessage>>(), null, Arg.Any<CancellationToken>())
            .Returns(new TestAsyncCollectionResult<StreamingChatCompletionUpdate>(
                [Update("partial", null)],
                midStreamException));
        await using var client = new OpenAIChatClient(chatClient);

        var act = () => CollectAsync(client.GenerateStreamingAsync("prompt", []));

        var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
        ex.Which.ProviderName.Should().Be("OpenAI");
        ex.Which.ErrorCode.Should().Be("http_503");
        ex.Which.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateStreamingAsync_NullContextEntries_SkippedInPromptAsync()
    {
        var chatClient = Substitute.For<ChatClient>();
        IEnumerable<ChatMessage>? capturedMessages = null;
        chatClient.CompleteChatStreamingAsync(
            Arg.Do<IEnumerable<ChatMessage>>(m => capturedMessages = m),
            null,
            Arg.Any<CancellationToken>())
            .Returns(new TestAsyncCollectionResult<StreamingChatCompletionUpdate>([
                Update("answer", ChatFinishReason.Stop),
            ]));
        await using var client = new OpenAIChatClient(chatClient);

        var chunks = await CollectAsync(client.GenerateStreamingAsync("q", [
            new RagChunk("id1", null!, null, "doc1", null),
            new RagChunk("id2", "   ", null, "doc1", null),
            new RagChunk("id3", "valid text", null, "doc1", null),
        ]));

        chunks.Should().HaveCountGreaterThan(0);
        capturedMessages.Should().NotBeNull();
        var userMsg = capturedMessages!.OfType<UserChatMessage>().Single();
        userMsg.Content.Should().ContainSingle(p => p.Text!.Contains("valid text"));
        userMsg.Content.Should().AllSatisfy(p => p.Text.Should().NotContain("null"));
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
        var client = new OpenAIChatClient(chatClient);
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
        var client = new OpenAIChatClient(chatClient);
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
        var client = new OpenAIChatClient(chatClient);

        await client.DisposeAsync();
        await client.DisposeAsync();

        true.Should().BeTrue();
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
