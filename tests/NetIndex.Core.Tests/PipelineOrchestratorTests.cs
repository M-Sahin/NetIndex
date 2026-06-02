#pragma warning disable CS1591
using Microsoft.Extensions.DependencyInjection;
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using NSubstitute;
using Xunit;

namespace NetIndex.Core.Tests;

/// <summary>
/// Pipeline contract tests for end-to-end stage coordination (Story 2.4).
/// </summary>
[Trait("Category", "PipelineContract")]
public sealed class PipelineOrchestratorTests
{
    // ── AC #1: IngestAsync → Chunk → Embed → Store ──

    [Fact]
    public async Task IngestAsync_WithDenyAllAuth_ThrowsAuthorizationExceptionAsync()
    {
        var pipeline = GetDenyAllPipeline();
        var document = CreateDocument("doc-1", "test content");

        var exception = await Assert.ThrowsAsync<NetIndexAuthorizationException>(
            () => pipeline.IngestAsync(document));

        Assert.Equal("No ITenantResolver configured. Access denied by default.", exception.Message);
    }

    [Fact]
    public async Task IngestAsync_WithValidAuth_CompletesPipelineAsync()
    {
        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks, withChunking: true);

        mocks.MockChunking!.ChunkAsync(Arg.Any<string>(), Arg.Any<ChunkingOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<RagChunk>>(
                [new RagChunk("chunk-0", "test content", null, "doc-1", null)]));

        mocks.MockEmbedding.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[][] { new float[384] }));

        var upsertChunks = new List<RagChunk>();
        mocks.MockStore.UpsertAsync(Arg.Do<IEnumerable<RagChunk>>(c => upsertChunks.AddRange(c)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var document = CreateDocument("doc-1", "test content");
        await pipeline.IngestAsync(document);

        Assert.Single(upsertChunks);
        Assert.Equal("doc-1", upsertChunks[0].DocumentId);
        Assert.Equal("doc-1_chunk_0", upsertChunks[0].Id);
        Assert.NotNull(upsertChunks[0].Embedding);
    }

    [Fact]
    public async Task IngestAsync_StageFailure_WrapsInNetIndexProviderExceptionAsync()
    {
        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks, withChunking: true);

        mocks.MockChunking!.ChunkAsync(Arg.Any<string>(), Arg.Any<ChunkingOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IEnumerable<RagChunk>>(new InvalidOperationException("upstream failure")));

        var document = CreateDocument("doc-1", "test");

        var exception = await Assert.ThrowsAsync<NetIndexProviderException>(
            () => pipeline.IngestAsync(document));

        Assert.Equal("Ingestion pipeline failed.", exception.Message);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public Task IngestAsync_WithDefaultChunking_StrategyIsOptionalAsync()
    {
        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks, withChunking: false);

        mocks.MockEmbedding.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[][] { new float[384] }));

        var document = CreateDocument("doc-1", "test content");
        return pipeline.IngestAsync(document); // should not throw
    }

    // ── AC #2: QueryAsync → Embed → Retrieve ──

    [Fact]
    public async Task QueryAsync_WithDenyAllAuth_ThrowsAuthorizationExceptionAsync()
    {
        var pipeline = GetDenyAllPipeline();

        var exception = await Assert.ThrowsAsync<NetIndexAuthorizationException>(
            async () => { await foreach (var _ in pipeline.QueryAsync("test query")) { } });

        Assert.Equal("No ITenantResolver configured. Access denied by default.", exception.Message);
    }

    [Fact]
    public async Task QueryAsync_WithValidAuth_ReturnsResultsAsync()
    {
        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks);

        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[384]));

        // Chunk must carry the tenant tag so the pipeline's tenant filter passes it through.
        var tenantMeta = new Dictionary<string, string> { [RagChunkMetadata.TenantId] = "test-tenant" };
        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(StreamSearchResultAsync(
                new RagChunk("chunk-1", "relevant content", new float[384], "doc-1", tenantMeta),
                0.95f, "doc-1"));

        var results = new List<SearchResult<RagChunk>>();
        await foreach (var result in pipeline.QueryAsync("test query"))
        {
            results.Add(result);
        }

        Assert.Single(results);
        Assert.Equal("doc-1", results[0].DocumentId);
        Assert.Equal("chunk-1", results[0].Item.Id);
        Assert.Equal(0.95f, results[0].Score);
    }

    [Fact]
    public Task QueryAsync_CancellationPropagatedAsync()
    {
        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks);

        var cts = new CancellationTokenSource();
        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<float[]>(new OperationCanceledException(cts.Token)));

        return Assert.ThrowsAsync<OperationCanceledException>(
            async () => { await foreach (var _ in pipeline.QueryAsync("test", cts.Token)) { } });
    }

    // ── AC #3: GenerateAsync → Query → Generate ──

    [Fact]
    public async Task GenerateAsync_WithDenyAllAuth_ThrowsAuthorizationExceptionAsync()
    {
        var pipeline = GetDenyAllPipeline();

        var exception = await Assert.ThrowsAsync<NetIndexAuthorizationException>(
            async () => { await foreach (var _ in pipeline.GenerateAsync("test query")) { } });

        Assert.Equal("No ITenantResolver configured. Access denied by default.", exception.Message);
    }

    [Fact]
    public async Task GenerateAsync_WithValidAuth_ReturnsStreamingChunksAsync()
    {
        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks);

        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[384]));

        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(StreamSearchResultAsync(
                new RagChunk("chunk-1", "context", new float[384], "doc-1", null), 0.9f, "doc-1"));

        mocks.MockChat.GenerateStreamingAsync(Arg.Any<string>(), Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(StubStreamAsync("Hello ", FinishReason.Stop));

        var chunks = new List<GenerationChunk>();
        await foreach (var chunk in pipeline.GenerateAsync("test query"))
        {
            chunks.Add(chunk);
        }

        Assert.NotEmpty(chunks);
        Assert.True(chunks[^1].IsComplete);
    }

    [Fact]
    public async Task GenerateAsync_FinalChunkHasIsCompleteTrueAsync()
    {
        var mocks = SetupMocksWithAuth();
        var pipeline = BuildPipeline(mocks);

        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[384]));

        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(StreamSearchResultAsync(
                new RagChunk("chunk-1", "context", new float[384], "doc-1", null), 0.9f, "doc-1"));

        mocks.MockChat.GenerateStreamingAsync(Arg.Any<string>(), Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(StubStreamMultiAsync("Token1 ", "Token2", FinishReason.Stop));

        var chunks = new List<GenerationChunk>();
        await foreach (var chunk in pipeline.GenerateAsync("test"))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(3, chunks.Count);
        Assert.False(chunks[0].IsComplete);
        Assert.False(chunks[1].IsComplete);
        Assert.True(chunks[2].IsComplete);
        Assert.Equal(FinishReason.Stop, chunks[2].FinishReason);
    }

    // ── AC #4 + #5: INetIndexPipeline registration ──

    [Fact]
    public void Build_RegistersINetIndexPipeline()
    {
        var services = new ServiceCollection();
        services.AddNetIndex().Build();

        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<INetIndexPipeline>();

        Assert.NotNull(pipeline);
        Assert.IsType<NetIndexPipeline>(pipeline);
    }

    [Fact]
    public void Build_ResolutionFromInterfaceAndConcrete_ReturnsSameInstance()
    {
        var services = new ServiceCollection();
        services.AddNetIndex().Build();

        using var provider = services.BuildServiceProvider();
        var byInterface = provider.GetRequiredService<INetIndexPipeline>();
        var byConcrete = provider.GetRequiredService<NetIndexPipeline>();

        Assert.Same(byInterface, byConcrete);
    }

    // ── Helpers ──

    private sealed class MockContext
    {
        public ITenantResolver MockResolver { get; } = Substitute.For<ITenantResolver>();
        public IChunkingStrategy? MockChunking { get; set; }
        public IEmbeddingGenerator MockEmbedding { get; } = Substitute.For<IEmbeddingGenerator>();
        public IVectorStore MockStore { get; } = Substitute.For<IVectorStore>();
        public IChatClient MockChat { get; } = Substitute.For<IChatClient>();
    }

    static MockContext SetupMocksWithAuth()
    {
        var mocks = new MockContext();
        mocks.MockResolver.ResolveTenantIdAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult("test-tenant"));
        mocks.MockEmbedding.Dimensions.Returns(384);
        mocks.MockStore.Dimensions.Returns(384);
        mocks.MockChat.GenerateStreamingAsync(Arg.Any<string>(), Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(StubStreamAsync("ok", FinishReason.Stop));
        return mocks;
    }

    static IDocument CreateDocument(string id, string content)
    {
        var doc = Substitute.For<IDocument>();
        doc.Id.Returns(id);
        doc.Content.Returns(content);
        return doc;
    }

    static INetIndexPipeline GetDenyAllPipeline()
    {
        var services = new ServiceCollection();
        services.AddNetIndex().Build();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<INetIndexPipeline>();
    }

    static INetIndexPipeline BuildPipeline(MockContext mocks, bool withChunking = false)
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
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<INetIndexPipeline>();
    }

    private static async IAsyncEnumerable<GenerationChunk> StubStreamAsync(
        string text,
        FinishReason reason,
        [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
    {
        yield return new GenerationChunk(text, false, reason);
        await Task.Yield();
        yield return new GenerationChunk(string.Empty, true, reason);
    }

    private static async IAsyncEnumerable<GenerationChunk> StubStreamMultiAsync(
        string text1,
        string text2,
        FinishReason reason,
        [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
    {
        yield return new GenerationChunk(text1, false, reason);
        await Task.Yield();
        yield return new GenerationChunk(text2, false, reason);
        await Task.Yield();
        yield return new GenerationChunk(string.Empty, true, reason);
    }

    private static async IAsyncEnumerable<SearchResult<RagChunk>> StreamSearchResultAsync(
        RagChunk chunk,
        float score,
        string documentId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
    {
        yield return new SearchResult<RagChunk>(chunk, score, documentId);
        await Task.Yield();
    }
}
