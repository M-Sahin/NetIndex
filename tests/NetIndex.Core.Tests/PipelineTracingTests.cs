#pragma warning disable CS1591
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using NetIndex.Core.Abstractions.Telemetry;
using NSubstitute;
using Xunit;

namespace NetIndex.Core.Tests;

/// <summary>
/// PipelineContract tests that verify OpenTelemetry tracing spans are emitted correctly
/// for all five RAG pipeline stages (Story 6.2).
/// </summary>
[Trait("Category", "PipelineContract")]
public sealed class PipelineTracingTests : IDisposable
{
    private readonly ConcurrentBag<Activity> _stopped = new();
    private readonly ActivityListener _listener;

    public PipelineTracingTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "NetIndex",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => _stopped.Add(a),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    // ── Span lookup helpers ──

    // Looks up a span by name WITHIN the given trace, isolating this test's spans
    // from any spans created by other tests running concurrently.
    private Activity? Span(string name, ActivityTraceId traceId) =>
        _stopped.FirstOrDefault(a => a.TraceId == traceId && a.OperationName == name);

    private static string? Tag(Activity a, string key) =>
        a.GetTagItem(key) as string;

    // Starts a root Activity that establishes a unique TraceId for span correlation.
    // The Activity object itself comes from the BCL legacy API (no ActivitySource required),
    // so it never appears in _stopped (which only captures "NetIndex"-source spans).
    private static Activity StartTestRoot()
    {
        var root = new Activity("test.root");
        root.Start();
        return root;
    }

    // ── AC-1 / AC-3: IngestAsync emits the right spans with non-zero duration ──

    [Fact]
    public async Task IngestAsync_EmitsIngestChunkEmbedSpans_WithExpectedTagsAsync()
    {
        using var root = StartTestRoot();
        var tid = root.TraceId;

        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks, withChunking: true);

        mocks.MockChunking!.ChunkAsync(Arg.Any<string>(), Arg.Any<ChunkingOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<RagChunk>>(
                [new RagChunk("c0", "text", null, "doc-1", null)]));
        mocks.MockEmbedding.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[][] { new float[384] }));
        mocks.MockStore.UpsertAsync(Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await pipeline.IngestAsync(CreateDocument("doc-1", "hello"));

        var ingest = Span(NetIndexSpanNames.Ingest, tid);
        var chunk = Span(NetIndexSpanNames.Chunk, tid);
        var embed = Span(NetIndexSpanNames.Embed, tid);

        Assert.NotNull(ingest);
        Assert.NotNull(chunk);
        Assert.NotNull(embed);

        // AC-5 tags — ingest span
        Assert.Equal("test-tenant", Tag(ingest, NetIndexSpanTags.TenantId));
        Assert.Equal("doc-1", Tag(ingest, NetIndexSpanTags.DocumentId));

        // AC-5 tags — chunk span
        Assert.Equal(1, (int)chunk.GetTagItem(NetIndexSpanTags.ChunkCount)!);

        // AC-5 tags — embed span
        Assert.Equal(1, (int)embed.GetTagItem(NetIndexSpanTags.EmbeddingCount)!);
        Assert.Equal(384, (int)embed.GetTagItem(NetIndexSpanTags.EmbeddingDimensions)!);

        // AC-3: all spans were stopped with non-zero duration
        Assert.True(ingest.Duration > TimeSpan.Zero);
        Assert.True(chunk.Duration > TimeSpan.Zero);
        Assert.True(embed.Duration > TimeSpan.Zero);
    }

    // ── AC-2: parent/child nesting for IngestAsync ──

    [Fact]
    public async Task IngestAsync_ChunkAndEmbedSpans_ParentedToIngestSpanAsync()
    {
        using var root = StartTestRoot();
        var tid = root.TraceId;

        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks, withChunking: true);

        mocks.MockChunking!.ChunkAsync(Arg.Any<string>(), Arg.Any<ChunkingOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<RagChunk>>(
                [new RagChunk("c0", "text", null, "doc-1", null)]));
        mocks.MockEmbedding.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[][] { new float[384] }));
        mocks.MockStore.UpsertAsync(Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await pipeline.IngestAsync(CreateDocument("doc-1", "hello"));

        var ingest = Span(NetIndexSpanNames.Ingest, tid)!;
        var chunk = Span(NetIndexSpanNames.Chunk, tid)!;
        var embed = Span(NetIndexSpanNames.Embed, tid)!;

        Assert.Equal(ingest.SpanId, chunk.ParentSpanId);
        Assert.Equal(ingest.SpanId, embed.ParentSpanId);
        Assert.Equal(ingest.TraceId, chunk.TraceId);
        Assert.Equal(ingest.TraceId, embed.TraceId);
    }

    // ── AC-4: error status on failing span and parent ──

    [Fact]
    public async Task IngestAsync_WhenEmbeddingFails_EmbedAndIngestSpansAreMarkedErrorAsync()
    {
        using var root = StartTestRoot();
        var tid = root.TraceId;

        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks, withChunking: true);

        mocks.MockChunking!.ChunkAsync(Arg.Any<string>(), Arg.Any<ChunkingOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<RagChunk>>(
                [new RagChunk("c0", "text", null, "doc-1", null)]));
        mocks.MockEmbedding.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<float[][]>(new InvalidOperationException("embed boom")));

        await Assert.ThrowsAsync<NetIndexProviderException>(() => pipeline.IngestAsync(CreateDocument("doc-1", "hello")));

        var embed = Span(NetIndexSpanNames.Embed, tid)!;
        var ingest = Span(NetIndexSpanNames.Ingest, tid)!;

        Assert.Equal(ActivityStatusCode.Error, embed.Status);
        Assert.Equal(ActivityStatusCode.Error, ingest.Status);
        Assert.Equal("Embedding generation failed", embed.StatusDescription);
        AssertExceptionEvent(embed, "System.InvalidOperationException", "embed boom");
        AssertExceptionEvent(ingest, "System.InvalidOperationException", "embed boom");

        // AC-3: span is stopped even on error
        Assert.True(embed.Duration > TimeSpan.Zero);
        Assert.True(ingest.Duration > TimeSpan.Zero);
    }

    // ── AC-1 / AC-5: QueryAsync emits embed + retrieve spans ──

    [Fact]
    public async Task QueryAsync_EmitsEmbedAndRetrieveSpans_WithTagsAsync()
    {
        using var root = StartTestRoot();
        var tid = root.TraceId;

        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks);

        var tenantMeta = new Dictionary<string, string> { [RagChunkMetadata.TenantId] = "test-tenant" };
        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[384]));
        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(StreamSearchResultAsync(new RagChunk("c1", "txt", new float[384], "doc-1", tenantMeta), 0.9f, "doc-1"));

        await foreach (var _ in pipeline.QueryAsync("q")) { }

        var embed = Span(NetIndexSpanNames.Embed, tid)!;
        var retrieve = Span(NetIndexSpanNames.Retrieve, tid)!;

        Assert.NotNull(embed);
        Assert.NotNull(retrieve);

        Assert.Equal(384, (int)embed.GetTagItem(NetIndexSpanTags.EmbeddingDimensions)!);

        Assert.Equal("test-tenant", Tag(retrieve, NetIndexSpanTags.TenantId));
        Assert.NotNull(retrieve.GetTagItem(NetIndexSpanTags.RetrieveTop));
        Assert.NotNull(retrieve.GetTagItem(NetIndexSpanTags.RetrieveResultCount));
        Assert.NotNull(retrieve.GetTagItem(NetIndexSpanTags.RetrieveFilteredCount));

        // AC-3: stopped with non-zero duration
        Assert.True(embed.Duration > TimeSpan.Zero);
        Assert.True(retrieve.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task QueryAsync_RetrieveSpan_HasResultCountAndFilteredCountTagsAsync()
    {
        using var root = StartTestRoot();
        var tid = root.TraceId;

        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks);

        var tenantMeta = new Dictionary<string, string> { [RagChunkMetadata.TenantId] = "test-tenant" };
        var otherMeta = new Dictionary<string, string> { [RagChunkMetadata.TenantId] = "other-tenant" };
        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[384]));
        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(StreamTwoResultsAsync(
                new RagChunk("c1", "t", new float[384], "doc-1", tenantMeta), 0.9f,
                new RagChunk("c2", "t", new float[384], "doc-2", otherMeta), 0.8f));

        await foreach (var _ in pipeline.QueryAsync("q")) { }

        var retrieve = Span(NetIndexSpanNames.Retrieve, tid)!;
        Assert.Equal(2, (int)retrieve.GetTagItem(NetIndexSpanTags.RetrieveResultCount)!);
        Assert.Equal(1, (int)retrieve.GetTagItem(NetIndexSpanTags.RetrieveFilteredCount)!);
    }

    [Fact]
    public async Task QueryAsync_RetrieveSpan_ReportsFilteredCountBeforeDefaultTopCapAsync()
    {
        using var root = StartTestRoot();
        var tid = root.TraceId;

        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks);

        var tenantMeta = new Dictionary<string, string> { [RagChunkMetadata.TenantId] = "test-tenant" };
        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[384]));
        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(StreamManyResultsAsync(tenantMeta, count: 7));

        var results = new List<SearchResult<RagChunk>>();
        await foreach (var result in pipeline.QueryAsync("q"))
        {
            results.Add(result);
        }

        var retrieve = Span(NetIndexSpanNames.Retrieve, tid)!;
        Assert.Equal(5, results.Count);
        Assert.Equal(7, (int)retrieve.GetTagItem(NetIndexSpanTags.RetrieveFilteredCount)!);
    }

    [Fact]
    public async Task QueryAsync_WhenVectorStoreFails_RetrieveSpanMarkedErrorAsync()
    {
        using var root = StartTestRoot();
        var tid = root.TraceId;

        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks);

        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[384]));
        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ThrowSearchResultsAsync(new InvalidOperationException("store boom")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => { await foreach (var _ in pipeline.QueryAsync("q")) { } });

        var retrieve = Span(NetIndexSpanNames.Retrieve, tid)!;
        Assert.Equal(ActivityStatusCode.Error, retrieve.Status);
        Assert.Equal("Retrieval failed", retrieve.StatusDescription);
        AssertExceptionEvent(retrieve, "System.InvalidOperationException", "store boom");
    }

    // ── AC-1 / AC-2 / AC-5: GenerateAsync emits generate span + nesting ──

    [Fact]
    public async Task GenerateAsync_EmitsGenerateSpan_WithTenantAndContextChunkCountTagsAsync()
    {
        using var root = StartTestRoot();
        var tid = root.TraceId;

        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks);

        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[384]));
        var tenantMeta = new Dictionary<string, string> { [RagChunkMetadata.TenantId] = "test-tenant" };
        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(StreamSearchResultAsync(new RagChunk("c1", "ctx", new float[384], "doc-1", tenantMeta), 0.9f, "doc-1"));
        mocks.MockChat.GenerateStreamingAsync(Arg.Any<string>(), Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(StubStreamAsync("hi", FinishReason.Stop));

        await foreach (var _ in pipeline.GenerateAsync("q")) { }

        var generate = Span(NetIndexSpanNames.Generate, tid)!;
        Assert.NotNull(generate);
        Assert.Equal("test-tenant", Tag(generate, NetIndexSpanTags.TenantId));
        Assert.Equal(1, (int)generate.GetTagItem(NetIndexSpanTags.ContextChunkCount)!);
        Assert.True(generate.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task GenerateAsync_EmbedAndRetrieveSpans_ParentedToGenerateSpanAsync()
    {
        using var root = StartTestRoot();
        var tid = root.TraceId;

        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks);

        var tenantMeta = new Dictionary<string, string> { [RagChunkMetadata.TenantId] = "test-tenant" };
        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[384]));
        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(StreamSearchResultAsync(new RagChunk("c1", "ctx", new float[384], "doc-1", tenantMeta), 0.9f, "doc-1"));
        mocks.MockChat.GenerateStreamingAsync(Arg.Any<string>(), Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(StubStreamAsync("hi", FinishReason.Stop));

        await foreach (var _ in pipeline.GenerateAsync("q")) { }

        var generate = Span(NetIndexSpanNames.Generate, tid)!;
        var embed = Span(NetIndexSpanNames.Embed, tid)!;
        var retrieve = Span(NetIndexSpanNames.Retrieve, tid)!;

        // embed and retrieve are children of generate (via Activity.Current ambient nesting)
        Assert.Equal(generate.SpanId, embed.ParentSpanId);
        Assert.Equal(generate.SpanId, retrieve.ParentSpanId);
        Assert.Equal(generate.TraceId, embed.TraceId);
        Assert.Equal(generate.TraceId, retrieve.TraceId);
    }

    // ── AC-4: generate span marked Error when context gathering fails ──

    [Fact]
    public async Task GenerateAsync_WhenEmbeddingFails_GenerateSpanMarkedErrorAsync()
    {
        using var root = StartTestRoot();
        var tid = root.TraceId;

        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks);

        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<float[]>(new InvalidOperationException("embed explode")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => { await foreach (var _ in pipeline.GenerateAsync("q")) { } });

        var generate = Span(NetIndexSpanNames.Generate, tid)!;
        Assert.NotNull(generate);
        Assert.Equal(ActivityStatusCode.Error, generate.Status);
        AssertExceptionEvent(generate, "System.InvalidOperationException", "embed explode");
        Assert.True(generate.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task GenerateAsync_WhenChatStreamFails_GenerateSpanMarkedErrorAsync()
    {
        using var root = StartTestRoot();
        var tid = root.TraceId;

        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks);

        var tenantMeta = new Dictionary<string, string> { [RagChunkMetadata.TenantId] = "test-tenant" };
        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[384]));
        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(StreamSearchResultAsync(new RagChunk("c1", "ctx", new float[384], "doc-1", tenantMeta), 0.9f, "doc-1"));
        mocks.MockChat.GenerateStreamingAsync(Arg.Any<string>(), Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(ThrowGenerationStreamAsync(new InvalidOperationException("chat boom")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => { await foreach (var _ in pipeline.GenerateAsync("q")) { } });

        var generate = Span(NetIndexSpanNames.Generate, tid)!;
        Assert.Equal(ActivityStatusCode.Error, generate.Status);
        AssertExceptionEvent(generate, "System.InvalidOperationException", "chat boom");
    }

    [Fact]
    public async Task IngestAsync_WhenAuthorizationFails_IngestSpanMarkedErrorAsync()
    {
        using var root = StartTestRoot();
        var tid = root.TraceId;

        var mocks = SetupMocksWithAuth();
        mocks.MockResolver.ResolveTenantIdAsync(Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new NetIndexAuthorizationException(
                "No tenant claim.", null, null, "MissingTenantIdClaim"));
        var pipeline = BuildPipeline(mocks, withChunking: true);

        await Assert.ThrowsAsync<NetIndexAuthorizationException>(() => pipeline.IngestAsync(CreateDocument("doc-1", "text")));

        var ingest = Span(NetIndexSpanNames.Ingest, tid)!;
        Assert.Equal(ActivityStatusCode.Error, ingest.Status);
        AssertExceptionEvent(ingest, "NetIndex.Core.Abstractions.NetIndexAuthorizationException", "No tenant claim.");
    }

    [Fact]
    public async Task IngestAsync_WhenEmbeddingBatchMismatch_EmbedSpanMarkedErrorAsync()
    {
        using var root = StartTestRoot();
        var tid = root.TraceId;

        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks, withChunking: true);

        mocks.MockChunking!.ChunkAsync(Arg.Any<string>(), Arg.Any<ChunkingOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<RagChunk>>(
                [new RagChunk("c0", "text", null, "doc-1", null)]));
        mocks.MockEmbedding.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Array.Empty<float[]>()));

        await Assert.ThrowsAsync<NetIndexProviderException>(() => pipeline.IngestAsync(CreateDocument("doc-1", "hello")));

        var embed = Span(NetIndexSpanNames.Embed, tid)!;
        Assert.Equal(ActivityStatusCode.Error, embed.Status);
        AssertExceptionEvent(embed, "NetIndex.Core.Abstractions.NetIndexProviderException", "Embedding batch returned 0 vectors for 1 chunks.");
    }

    [Fact]
    public async Task IngestAsync_RecordsUpsertChunkCountOnIngestSpanAsync()
    {
        using var root = StartTestRoot();
        var tid = root.TraceId;

        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks, withChunking: true);

        mocks.MockChunking!.ChunkAsync(Arg.Any<string>(), Arg.Any<ChunkingOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<RagChunk>>(
                [new RagChunk("c0", "text", null, "doc-1", null)]));
        mocks.MockEmbedding.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[][] { new float[384] }));
        mocks.MockStore.UpsertAsync(Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await pipeline.IngestAsync(CreateDocument("doc-1", "hello"));

        var ingest = Span(NetIndexSpanNames.Ingest, tid)!;
        Assert.Equal(1, (int)ingest.GetTagItem(NetIndexSpanTags.ChunkCount)!);
        Assert.Contains(ingest.Events, e =>
            e.Name == "netindex.upsert" &&
            e.Tags.Any(t => t.Key == NetIndexSpanTags.ChunkCount && t.Value is int value && value == 1));
    }

    // ── AC-6: no listener → pipeline is unchanged (null activities are no-ops) ──

    [Fact]
    public async Task NoListener_IngestAsync_CompletesWithoutNullReferenceExceptionAsync()
    {
        // Dispose our listener so StartActivity returns null for new operations,
        // verifying that all activity?.foo() null-conditional calls are safe.
        _listener.Dispose();
        Assert.False(NetIndexActivitySource.Source.HasListeners());

        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks, withChunking: true);

        mocks.MockChunking!.ChunkAsync(Arg.Any<string>(), Arg.Any<ChunkingOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<RagChunk>>(
                [new RagChunk("c0", "text", null, "doc-1", null)]));
        mocks.MockEmbedding.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[][] { new float[384] }));
        mocks.MockStore.UpsertAsync(Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Must complete without NullReferenceException even though StartActivity returns null
        var ex = await Record.ExceptionAsync(() => pipeline.IngestAsync(CreateDocument("doc-1", "text")));
        Assert.Null(ex);
    }

    // ── Helpers ──

    private static void AssertExceptionEvent(Activity activity, string exceptionType, string exceptionMessage)
    {
        var exceptionEvent = Assert.Single(activity.Events, e => e.Name == "exception");
        Assert.Contains(exceptionEvent.Tags, t => t.Key == "exception.type" && string.Equals(t.Value as string, exceptionType, StringComparison.Ordinal));
        Assert.Contains(exceptionEvent.Tags, t => t.Key == "exception.message" && string.Equals(t.Value as string, exceptionMessage, StringComparison.Ordinal));
    }

    private sealed class MockContext
    {
        public ITenantResolver MockResolver { get; } = Substitute.For<ITenantResolver>();
        public IChunkingStrategy? MockChunking { get; set; }
        public IEmbeddingGenerator MockEmbedding { get; } = Substitute.For<IEmbeddingGenerator>();
        public IVectorStore MockStore { get; } = Substitute.For<IVectorStore>();
        public IChatClient MockChat { get; } = Substitute.For<IChatClient>();
    }

    private static MockContext SetupMocksWithAuth()
    {
        var mocks = new MockContext();
        mocks.MockResolver.ResolveTenantIdAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("test-tenant"));
        mocks.MockEmbedding.Dimensions.Returns(384);
        mocks.MockStore.Dimensions.Returns(384);
        mocks.MockChat.GenerateStreamingAsync(Arg.Any<string>(), Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(StubStreamAsync("ok", FinishReason.Stop));
        return mocks;
    }

    private static INetIndexPipeline BuildPipeline(MockContext mocks, bool withChunking = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantResolver>(mocks.MockResolver);
        if (withChunking)
        {
            mocks.MockChunking = Substitute.For<IChunkingStrategy>();
            services.AddSingleton<IChunkingStrategy>(mocks.MockChunking);
        }
        services.AddSingleton<IEmbeddingGenerator>(mocks.MockEmbedding);
        services.AddSingleton<IVectorStore>(mocks.MockStore);
        services.AddSingleton<IChatClient>(mocks.MockChat);
        services.AddNetIndex().Build();
        return services.BuildServiceProvider().GetRequiredService<INetIndexPipeline>();
    }

    private static IDocument CreateDocument(string id, string content)
    {
        var doc = Substitute.For<IDocument>();
        doc.Id.Returns(id);
        doc.Content.Returns(content);
        return doc;
    }

    private static async IAsyncEnumerable<GenerationChunk> StubStreamAsync(
        string text,
        FinishReason reason,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new GenerationChunk(text, false, reason);
        await Task.Yield();
        yield return new GenerationChunk(string.Empty, true, reason);
    }

    private static async IAsyncEnumerable<SearchResult<RagChunk>> StreamSearchResultAsync(
        RagChunk chunk,
        float score,
        string documentId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new SearchResult<RagChunk>(chunk, score, documentId);
        await Task.Yield();
    }

    private static async IAsyncEnumerable<SearchResult<RagChunk>> StreamTwoResultsAsync(
        RagChunk chunk1, float score1,
        RagChunk chunk2, float score2,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new SearchResult<RagChunk>(chunk1, score1, chunk1.DocumentId ?? "");
        await Task.Yield();
        yield return new SearchResult<RagChunk>(chunk2, score2, chunk2.DocumentId ?? "");
        await Task.Yield();
    }

    private static async IAsyncEnumerable<SearchResult<RagChunk>> StreamManyResultsAsync(
        IReadOnlyDictionary<string, string> metadata,
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < count; i++)
        {
            yield return new SearchResult<RagChunk>(
                new RagChunk($"c{i}", "t", new float[384], $"doc-{i}", metadata),
                1.0f - (i * 0.01f),
                $"doc-{i}");
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<SearchResult<RagChunk>> ThrowSearchResultsAsync(
        Exception exception,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        throw exception;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<GenerationChunk> ThrowGenerationStreamAsync(
        Exception exception,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        throw exception;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
