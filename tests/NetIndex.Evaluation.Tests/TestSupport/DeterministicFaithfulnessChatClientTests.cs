using NetIndex.Core.Abstractions;

namespace NetIndex.Evaluation.Tests.TestSupport;

public class DeterministicFaithfulnessChatClientTests
{
    private static readonly IReadOnlyDictionary<string, string> NoMetadata =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static RagChunk Chunk(string id, string text) =>
        new(id, text, Embedding: null, DocumentId: id, Metadata: NoMetadata);

    // ── Determinism ──

    [Fact]
    public async Task GenerateStreamingAsync_SameInputs_ProducesIdenticalAnswersAsync()
    {
        var client = new DeterministicFaithfulnessChatClient();
        var chunks = new[] { Chunk("c1", "vector store cosine ranking") };

        var first = await CollectAnswerAsync(client, "query text", chunks);
        var second = await CollectAnswerAsync(client, "query text", chunks);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GenerateStreamingAsync_DifferentContext_ProducesDifferentAnswersAsync()
    {
        var client = new DeterministicFaithfulnessChatClient();

        var firstAnswer = await CollectAnswerAsync(client, "query",
            [Chunk("c1", "vector embedding cosine similarity ranking")]);

        var secondAnswer = await CollectAnswerAsync(client, "query",
            [Chunk("c2", "tenant resolver authorization deny pipeline")]);

        Assert.NotEqual(firstAnswer, secondAnswer);
    }

    [Fact]
    public async Task GenerateStreamingAsync_DifferentQuerySameContext_ProducesDifferentAnswerOrderAsync()
    {
        var client = new DeterministicFaithfulnessChatClient();
        var context = new[] { Chunk("c1", "vector ranking tenant authorization") };

        var retrievalAnswer = await CollectAnswerAsync(client, "How does vector ranking work?", context);
        var authAnswer = await CollectAnswerAsync(client, "How does tenant authorization work?", context);

        Assert.NotEqual(retrievalAnswer, authAnswer);
        Assert.StartsWith("vector ranking", retrievalAnswer, StringComparison.Ordinal);
        Assert.StartsWith("tenant authorization", authAnswer, StringComparison.Ordinal);
    }

    // ── Streaming shape ──

    [Fact]
    public async Task GenerateStreamingAsync_EmitsMultipleNonTerminalChunks_ThenExactlyOneTerminalChunkAsync()
    {
        var client = new DeterministicFaithfulnessChatClient();
        RagChunk[] chunks = [Chunk("c1", "vector embedding cosine similarity ranking order")];

        var allChunks = new List<GenerationChunk>();
        await foreach (var chunk in client.GenerateStreamingAsync("query", chunks))
        {
            allChunks.Add(chunk);
        }

        Assert.True(allChunks.Count >= 2, $"Expected at least 2 chunks but got {allChunks.Count}");
        var terminal = Assert.Single(allChunks.Where(c => c.IsComplete));
        Assert.Equal(string.Empty, terminal.Text);
        Assert.All(allChunks.Where(c => !c.IsComplete), c => Assert.False(c.IsComplete));
    }

    [Fact]
    public async Task GenerateStreamingAsync_EmptyContext_EmitsOnlyTerminalChunkAsync()
    {
        var client = new DeterministicFaithfulnessChatClient();

        var allChunks = new List<GenerationChunk>();
        await foreach (var chunk in client.GenerateStreamingAsync("query", []))
        {
            allChunks.Add(chunk);
        }

        var terminal = Assert.Single(allChunks);
        Assert.True(terminal.IsComplete);
        Assert.Equal(string.Empty, terminal.Text);
    }

    // ── Context capture ──

    [Fact]
    public async Task GenerateStreamingAsync_CapturesContextChunksByPromptAsync()
    {
        var client = new DeterministicFaithfulnessChatClient();
        var expected = new[] { Chunk("c1", "some content"), Chunk("c2", "more content") };

        await foreach (var _ in client.GenerateStreamingAsync("query", expected)) { }

        var captured = client.GetCapturedChunks("query");
        Assert.Equal(expected.Length, captured.Count);
        Assert.Equal(expected[0].Id, captured[0].Id);
        Assert.Equal(expected[1].Id, captured[1].Id);
    }

    [Fact]
    public void GetCapturedChunks_BeforeAnyCall_Throws()
    {
        var client = new DeterministicFaithfulnessChatClient();

        Assert.Throws<InvalidOperationException>(() => client.GetCapturedChunks("query"));
    }

    [Fact]
    public async Task GenerateStreamingAsync_SubsequentCall_UpdatesPromptCaptureAsync()
    {
        var client = new DeterministicFaithfulnessChatClient();
        var first = new[] { Chunk("c1", "first content") };
        var second = new[] { Chunk("c2", "second content") };

        await foreach (var _ in client.GenerateStreamingAsync("q", first)) { }
        await foreach (var _ in client.GenerateStreamingAsync("q", second)) { }

        Assert.Equal("c2", client.GetCapturedChunks("q").Single().Id);
    }

    [Fact]
    public async Task ResetCapture_RemovesPromptCaptureAsync()
    {
        var client = new DeterministicFaithfulnessChatClient();
        await foreach (var _ in client.GenerateStreamingAsync("q", [Chunk("c1", "first content")])) { }

        client.ResetCapture("q");

        Assert.Throws<InvalidOperationException>(() => client.GetCapturedChunks("q"));
    }

    // ── Cancellation ──

    [Fact]
    public async Task GenerateStreamingAsync_TokenCancelledBeforeEnumeration_ThrowsAsync()
    {
        var client = new DeterministicFaithfulnessChatClient();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in client.GenerateStreamingAsync("query",
                [Chunk("c1", "vector embedding cosine ranking")], cts.Token))
            {
            }
        });
    }

    [Fact]
    public async Task GenerateStreamingAsync_TokenCancelledDuringEnumeration_ThrowsAsync()
    {
        var client = new DeterministicFaithfulnessChatClient();
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var chunk in client.GenerateStreamingAsync("query",
                [Chunk("c1", "vector embedding cosine similarity ranking order retrieval")],
                cts.Token))
            {
                if (!chunk.IsComplete)
                {
                    await cts.CancelAsync();
                }
            }
        });
    }

    [Fact]
    public async Task GenerateStreamingAsync_PreCancelledEnumeratorToken_ThrowsAsync()
    {
        var client = new DeterministicFaithfulnessChatClient();
        using var cts = new CancellationTokenSource();
        var stream = client.GenerateStreamingAsync("query",
            [Chunk("c1", "vector embedding cosine")], CancellationToken.None);
        await cts.CancelAsync();

        await using var enumerator = stream.GetAsyncEnumerator(cts.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await enumerator.MoveNextAsync());
    }

    // ── Helpers ──

    private static async Task<string> CollectAnswerAsync(
        DeterministicFaithfulnessChatClient client,
        string query,
        IEnumerable<RagChunk> context)
    {
        var sb = new System.Text.StringBuilder();
        await foreach (var chunk in client.GenerateStreamingAsync(query, context))
        {
            if (!string.IsNullOrEmpty(chunk.Text))
            {
                sb.Append(chunk.Text);
            }
        }

        return sb.ToString();
    }
}
