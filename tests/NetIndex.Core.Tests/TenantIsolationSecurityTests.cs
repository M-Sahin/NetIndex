#pragma warning disable CS1591
using Microsoft.Extensions.DependencyInjection;
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using NSubstitute;
using Xunit;

namespace NetIndex.Core.Tests;

/// <summary>
/// Security-contract tests proving tenant isolation is enforced by the pipeline (Story 6.1).
/// All tests must be deterministic and require no external infrastructure.
/// </summary>
[Trait("Category", "SecurityContract")]
public sealed class TenantIsolationSecurityTests
{
    // ── AC-Core-1: tenant A never sees tenant B's documents ──

    [Fact]
    public async Task QueryAsync_TenantA_DoesNotSee_TenantBChunksAsync()
    {
        // Arrange: resolver resolves to "tenant-a"
        var mocks = BuildMocksWithTenant("tenant-a");
        var pipeline = BuildPipeline(mocks);

        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[384]));

        // Store returns one chunk tagged tenant-a and one tagged tenant-b
        var tenantAChunk = MakeChunk("a-1", "tenant-a");
        var tenantBChunk = MakeChunk("b-1", "tenant-b");
        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(StreamSearchResultsAsync(
                (tenantAChunk, 0.9f, "doc-a"),
                (tenantBChunk, 0.8f, "doc-b")));

        // Act
        var results = new List<SearchResult<RagChunk>>();
        await foreach (var r in pipeline.QueryAsync("query"))
        {
            results.Add(r);
        }

        // Assert: only the tenant-a chunk passes through
        Assert.Single(results);
        Assert.Equal("a-1", results[0].Item.Id);
    }

    [Fact]
    public async Task QueryAsync_ChunkWithNoTenantTag_IsExcludedAsync()
    {
        var mocks = BuildMocksWithTenant("tenant-a");
        var pipeline = BuildPipeline(mocks);

        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[384]));

        // Chunk has no metadata at all (no tenant tag)
        var untaggedChunk = new RagChunk("no-tag-1", "text", new float[384], "doc-x", null);
        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(StreamSearchResultsAsync((untaggedChunk, 0.95f, "doc-x")));

        var results = new List<SearchResult<RagChunk>>();
        await foreach (var r in pipeline.QueryAsync("query"))
        {
            results.Add(r);
        }

        Assert.Empty(results);
    }

    // ── AC-Core-2: ingest stamps the tenant tag ──

    [Fact]
    public async Task IngestAsync_StampsTenantIdOnEveryChunkAsync()
    {
        var mocks = BuildMocksWithTenant("corp");
        var pipeline = BuildPipeline(mocks);

        mocks.MockChunking.ChunkAsync(Arg.Any<string>(), Arg.Any<ChunkingOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<RagChunk>>(
            [
                new RagChunk("c0", "text", null, "doc-1", null),
                new RagChunk("c1", "text2", null, "doc-1", null),
            ]));
        mocks.MockEmbedding.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[][] { new float[384], new float[384] }));

        var upsertedChunks = new List<RagChunk>();
        mocks.MockStore.UpsertAsync(Arg.Do<IEnumerable<RagChunk>>(c => upsertedChunks.AddRange(c)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await pipeline.IngestAsync(CreateDocument("doc-1", "content"));

        Assert.Equal(2, upsertedChunks.Count);
        Assert.All(upsertedChunks, chunk =>
        {
            Assert.NotNull(chunk.Metadata);
            Assert.Equal("corp", chunk.Metadata[RagChunkMetadata.TenantId]);
        });
    }

    [Fact]
    public async Task IngestAsync_RejectsChunk_WhenReservedKeyPresetAsync()
    {
        var mocks = BuildMocksWithTenant("corp");
        var pipeline = BuildPipeline(mocks);

        // Caller tries to pre-set the reserved tenant key
        var reservedMeta = new Dictionary<string, string>
        {
            [RagChunkMetadata.TenantId] = "evil-tenant",
        };
        mocks.MockChunking.ChunkAsync(Arg.Any<string>(), Arg.Any<ChunkingOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<RagChunk>>(
            [
                new RagChunk("c0", "text", null, "doc-1", reservedMeta),
            ]));
        mocks.MockEmbedding.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[][] { new float[384] }));

        var ex = await Assert.ThrowsAsync<NetIndexAuthorizationException>(
            () => pipeline.IngestAsync(CreateDocument("doc-1", "content")));

        Assert.Equal("ReservedMetadataKeyConflict", ex.FailureReason);
    }

    // ── AC-Core-3: missing tenant claim is denied ──

    [Fact]
    public async Task QueryAsync_MissingTenantClaim_ThrowsAuthorizationExceptionAsync()
    {
        var mocks = new MockContext();
        mocks.MockEmbedding.Dimensions.Returns(384);
        mocks.MockStore.Dimensions.Returns(384);
        mocks.MockResolver.ResolveTenantIdAsync(Arg.Any<CancellationToken>())
            .Returns<Task<string>>(x => throw new NetIndexAuthorizationException(
                "No tenant claim.", null, null, "MissingTenantIdClaim"));

        var pipeline = BuildPipeline(mocks);

        var ex = await Assert.ThrowsAsync<NetIndexAuthorizationException>(
            async () => { await foreach (var _ in pipeline.QueryAsync("q")) { } });

        Assert.Equal("MissingTenantIdClaim", ex.FailureReason);
    }

    [Fact]
    public async Task GenerateAsync_MissingTenantClaim_ThrowsAuthorizationExceptionAsync()
    {
        var mocks = new MockContext();
        mocks.MockEmbedding.Dimensions.Returns(384);
        mocks.MockStore.Dimensions.Returns(384);
        mocks.MockResolver.ResolveTenantIdAsync(Arg.Any<CancellationToken>())
            .Returns<Task<string>>(x => throw new NetIndexAuthorizationException(
                "No tenant claim.", null, null, "MissingTenantIdClaim"));

        var pipeline = BuildPipeline(mocks);

        var ex = await Assert.ThrowsAsync<NetIndexAuthorizationException>(
            async () => { await foreach (var _ in pipeline.GenerateAsync("q")) { } });

        Assert.Equal("MissingTenantIdClaim", ex.FailureReason);
    }

    [Fact]
    public async Task GenerateAsync_TenantA_DoesNotSee_TenantBChunksAsync()
    {
        // Arrange: resolver resolves to "tenant-a"
        var mocks = BuildMocksWithTenant("tenant-a");
        var pipeline = BuildPipeline(mocks);

        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[384]));

        var tenantAChunk = MakeChunk("a-1", "tenant-a");
        var tenantBChunk = MakeChunk("b-1", "tenant-b");
        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(StreamSearchResultsAsync(
                (tenantAChunk, 0.9f, "doc-a"),
                (tenantBChunk, 0.8f, "doc-b")));

        // Capture the context chunks forwarded to the LLM.
        List<RagChunk>? capturedContext = null;
        mocks.MockChat.GenerateStreamingAsync(
            Arg.Any<string>(),
            Arg.Do<IEnumerable<RagChunk>>(ctx => capturedContext = ctx.ToList()),
            Arg.Any<CancellationToken>())
            .Returns(EmptyGenerationStreamAsync());

        // Act
        await foreach (var _ in pipeline.GenerateAsync("query")) { }

        // Assert: only the tenant-a chunk reaches the LLM context (Task 10, AC-Core-1 Generate path)
        Assert.NotNull(capturedContext);
        Assert.Single(capturedContext);
        Assert.Equal("a-1", capturedContext[0].Id);
    }

    // ── Over-fetch anti-starvation: tenant B chunks do not starve tenant A ──

    [Fact]
    public async Task QueryAsync_OverFetch_PreventsStarvation_WhenTenantBChunksDominateGlobalTopAsync()
    {
        var mocks = BuildMocksWithTenant("tenant-a");
        var pipeline = BuildPipeline(mocks);

        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[384]));

        // Store returns 4 high-scoring tenant-b chunks followed by 1 tenant-a chunk.
        // Without over-fetch (top=5), the global top-5 would include all 4 tenant-b chunks
        // and the 1 tenant-a chunk — but only if we fetch >= 5. With OverFetchFactor=5,
        // we request top=25, so the tenant-a chunk is fetched despite tenant-b domination.
        var items = new List<(RagChunk, float, string)>();
        for (var i = 0; i < 4; i++)
        {
            items.Add((MakeChunk($"b-{i}", "tenant-b"), 0.99f - i * 0.01f, "doc-b"));
        }
        items.Add((MakeChunk("a-0", "tenant-a"), 0.80f, "doc-a"));

        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(StreamSearchResultsAsync(items.ToArray()));

        var results = new List<SearchResult<RagChunk>>();
        await foreach (var r in pipeline.QueryAsync("query"))
        {
            results.Add(r);
        }

        // Tenant-a chunk must be returned (not starved by tenant-b dominance)
        Assert.Single(results);
        Assert.Equal("a-0", results[0].Item.Id);
    }

    // ── Helpers ──

    private static MockContext BuildMocksWithTenant(string tenantId)
    {
        var mocks = new MockContext();
        mocks.MockResolver.ResolveTenantIdAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(tenantId));
        mocks.MockEmbedding.Dimensions.Returns(384);
        mocks.MockStore.Dimensions.Returns(384);
        mocks.MockChat.GenerateStreamingAsync(
            Arg.Any<string>(), Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(EmptyGenerationStreamAsync());
        return mocks;
    }

    private static INetIndexPipeline BuildPipeline(MockContext mocks)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantResolver>(mocks.MockResolver);
        services.AddSingleton<IChunkingStrategy>(mocks.MockChunking);
        services.AddSingleton<IEmbeddingGenerator>(mocks.MockEmbedding);
        services.AddSingleton<IVectorStore>(mocks.MockStore);
        services.AddSingleton<IChatClient>(mocks.MockChat);
        services.AddNetIndex().Build();
        return services.BuildServiceProvider().GetRequiredService<INetIndexPipeline>();
    }

    private static async IAsyncEnumerable<GenerationChunk> EmptyGenerationStreamAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    private static RagChunk MakeChunk(string id, string tenantId)
    {
        var meta = new Dictionary<string, string> { [RagChunkMetadata.TenantId] = tenantId };
        return new RagChunk(id, "text", new float[384], "doc-1", meta);
    }

    private static IDocument CreateDocument(string id, string content)
    {
        var doc = Substitute.For<IDocument>();
        doc.Id.Returns(id);
        doc.Content.Returns(content);
        return doc;
    }

    private static async IAsyncEnumerable<SearchResult<RagChunk>> StreamSearchResultsAsync(
        params (RagChunk Chunk, float Score, string DocumentId)[] items)
    {
        foreach (var (chunk, score, docId) in items)
        {
            yield return new SearchResult<RagChunk>(chunk, score, docId);
            await Task.Yield();
        }
    }

    private sealed class MockContext
    {
        public ITenantResolver MockResolver { get; } = Substitute.For<ITenantResolver>();
        public IChunkingStrategy MockChunking { get; } = Substitute.For<IChunkingStrategy>();
        public IEmbeddingGenerator MockEmbedding { get; } = Substitute.For<IEmbeddingGenerator>();
        public IVectorStore MockStore { get; } = Substitute.For<IVectorStore>();
        public IChatClient MockChat { get; } = Substitute.For<IChatClient>();
    }
}
