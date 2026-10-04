#pragma warning disable CS1591
using Microsoft.Extensions.DependencyInjection;
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion;
using NetIndex.Testing.Common;
using NSubstitute;

namespace NetIndex.Integration.Tests;

/// <summary>
/// Story 2.12 regression tests: configured chunk sizes reach the pipeline, and re-ingest replaces a
/// document's chunk set atomically without stale chunks or cross-tenant overwrites.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ChunkingAndReIngestTests
{
    private sealed class MutableTenant
    {
        public string Id { get; set; } = "tenant-a";
    }

    private static ITenantResolver CreateResolver(MutableTenant tenant)
    {
        var resolver = Substitute.For<ITenantResolver>();
        resolver.ResolveTenantIdAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(tenant.Id));
        resolver.ResolveClaimsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()));
        return resolver;
    }

    private static IDocument CreateDocument(string id, string content)
    {
        var doc = Substitute.For<IDocument>();
        doc.Id.Returns(id);
        doc.Content.Returns(content);
        doc.Metadata.Returns(new Dictionary<string, string>());
        return doc;
    }

    private static ServiceProvider BuildProvider(MutableTenant tenant, Action<INetIndexBuilder>? configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateResolver(tenant));
        services.AddSingleton<IEmbeddingGenerator>(new FakeEmbeddingGenerator(384));
        services.AddNetIndex(configure ?? (_ => { })).Build();
        return services.BuildServiceProvider();
    }

    private static async Task<List<RagChunk>> AllChunksAsync(ServiceProvider provider)
    {
        var store = provider.GetRequiredService<IVectorStore>();
        var embedder = provider.GetRequiredService<IEmbeddingGenerator>();
        var vector = await embedder.GenerateAsync("anything");
        var chunks = new List<RagChunk>();
        await foreach (var hit in store.QueryAsync(vector, top: 1000))
        {
            chunks.Add(hit.Item);
        }

        return chunks;
    }

    private static string Paragraphs(int totalChars)
    {
        var paragraph = string.Concat(Enumerable.Repeat("Sentence about retrieval. ", 10)).TrimEnd();
        var sb = new System.Text.StringBuilder();
        while (sb.Length < totalChars)
        {
            sb.Append(paragraph).Append("\n\n");
        }

        return sb.ToString(0, totalChars);
    }

    private static string Lines(int count) =>
        string.Join("\n", Enumerable.Range(0, count).Select(i => new string((char)('a' + i), 30)));

    [Fact]
    public async Task ConfiguredSize_FixedSize200_IsHonouredWithParagraphsAsync()
    {
        var tenant = new MutableTenant();
        using var provider = BuildProvider(tenant, b => b.UseChunking(c => c.FixedSize(200, 20)));
        await provider.GetRequiredService<INetIndexPipeline>().IngestAsync(CreateDocument("doc-1", Paragraphs(10_000)));

        var chunks = await AllChunksAsync(provider);

        Assert.True(chunks.Count > 10);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 800, $"chunk of {c.Text.Length} chars exceeds 800"));
    }

    [Fact]
    public async Task ConfiguredSize_FixedSize200_IsHonouredWithoutParagraphBreaksAsync()
    {
        var tenant = new MutableTenant();
        using var provider = BuildProvider(tenant, b => b.UseChunking(c => c.FixedSize(200, 20)));
        var noBreaks = string.Concat(Enumerable.Repeat("Sentence about retrieval. ", 400)).TrimEnd();
        await provider.GetRequiredService<INetIndexPipeline>().IngestAsync(CreateDocument("doc-1", noBreaks));

        var chunks = await AllChunksAsync(provider);

        Assert.True(chunks.Count > 10);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 800));
    }

    [Fact]
    public async Task ConfiguredSize_NoSeparatorsAtAll_FallsBackToHardCharacterSplitAsync()
    {
        var tenant = new MutableTenant();
        using var provider = BuildProvider(tenant, b => b.UseChunking(c => c.FixedSize(200, 20)));
        await provider.GetRequiredService<INetIndexPipeline>().IngestAsync(CreateDocument("doc-1", new string('x', 10_000)));

        var chunks = await AllChunksAsync(provider);

        Assert.True(chunks.Count >= 13);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 800));
    }

    [Fact]
    public async Task UseChunking_WithoutArguments_AppliesConfigurationDefaultsAsync()
    {
        var tenant = new MutableTenant();
        using var provider = BuildProvider(tenant, b => b.UseChunking());

        var options = provider.GetRequiredService<ChunkingOptions>();
        Assert.Equal(new ChunkingOptions(512, 64, "\n"), options);

        await provider.GetRequiredService<INetIndexPipeline>().IngestAsync(CreateDocument("doc-1", Paragraphs(10_000)));
        var chunks = await AllChunksAsync(provider);

        // Default 512 tokens = 2048 chars; the forced 1000-token default would have allowed 4000.
        Assert.True(chunks.Count > 4);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 2048));
    }

    [Fact]
    public async Task WithoutUseChunking_PassThroughBehaviourIsUnchangedAsync()
    {
        var tenant = new MutableTenant();
        using var provider = BuildProvider(tenant, configure: null);

        Assert.Null(provider.GetService<ChunkingOptions>());
        await provider.GetRequiredService<INetIndexPipeline>().IngestAsync(CreateDocument("doc-1", "one chunk"));

        var chunks = await AllChunksAsync(provider);
        Assert.Equal("doc-1_chunk_0", Assert.Single(chunks).Id);
    }

    [Theory]
    [InlineData(5, 2)]
    [InlineData(2, 2)]
    [InlineData(2, 5)]
    public async Task ReIngest_LeavesExactlyTheNewChunkSetAsync(int firstCount, int secondCount)
    {
        var tenant = new MutableTenant();

        // 10 tokens = 40 chars; one 30-char line per chunk with separator "\n".
        using var provider = BuildProvider(tenant, b => b.UseChunking(c => c.FixedSize(10, 0)));
        var pipeline = provider.GetRequiredService<INetIndexPipeline>();

        await pipeline.IngestAsync(CreateDocument("doc-1", Lines(firstCount)));
        Assert.Equal(firstCount, (await AllChunksAsync(provider)).Count);

        await pipeline.IngestAsync(CreateDocument("doc-1", Lines(secondCount)));
        var chunks = await AllChunksAsync(provider);

        Assert.Equal(secondCount, chunks.Count);
        Assert.Equal(
            Enumerable.Range(0, secondCount).Select(i => $"doc-1_chunk_{i}").OrderBy(x => x, StringComparer.Ordinal),
            chunks.Select(c => c.Id).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ReIngest_ByAnotherTenant_IsRejectedAndLeavesTheOriginalUntouchedAsync()
    {
        var tenant = new MutableTenant { Id = "tenant-a" };
        using var provider = BuildProvider(tenant, b => b.UseChunking(c => c.FixedSize(10, 0)));
        var pipeline = provider.GetRequiredService<INetIndexPipeline>();

        await pipeline.IngestAsync(CreateDocument("doc-1", Lines(3)));

        tenant.Id = "tenant-b";
        var exception = await Assert.ThrowsAsync<NetIndexAuthorizationException>(
            () => pipeline.IngestAsync(CreateDocument("doc-1", "tenant b content")));
        Assert.Equal(IVectorStore.CROSS_TENANT_DOCUMENT_COLLISION, exception.FailureReason);

        var chunks = await AllChunksAsync(provider);
        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, c => Assert.Equal("tenant-a", c.Metadata![RagChunkMetadata.TenantId]));
    }

    private sealed class EmptyChunkingStrategy : IChunkingStrategy
    {
        public Task<IEnumerable<RagChunk>> ChunkAsync(string text, ChunkingOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<RagChunk>>(Array.Empty<RagChunk>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public async Task ReIngest_WithEmptyContent_IsRejectedAndLeavesExistingChunksUntouchedAsync(string content)
    {
        var tenant = new MutableTenant();
        using var provider = BuildProvider(tenant, b => b.UseChunking(c => c.FixedSize(10, 0)));
        var pipeline = provider.GetRequiredService<INetIndexPipeline>();
        await pipeline.IngestAsync(CreateDocument("doc-1", Lines(3)));

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => pipeline.IngestAsync(CreateDocument("doc-1", content)));
        Assert.IsNotType<NetIndexAuthorizationException>(exception);

        var chunks = await AllChunksAsync(provider);
        Assert.Equal(3, chunks.Count);
    }

    [Fact]
    public async Task ReIngest_ThatChunksToNothing_IsRejectedAndLeavesExistingChunksUntouchedAsync()
    {
        var tenant = new MutableTenant();
        var services = new ServiceCollection();
        services.AddSingleton(CreateResolver(tenant));
        services.AddSingleton<IEmbeddingGenerator>(new FakeEmbeddingGenerator(384));
        services.AddNetIndex(b => b.UseChunking(c => c.FixedSize(10, 0))).Build();
        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<INetIndexPipeline>();
        await pipeline.IngestAsync(CreateDocument("doc-1", Lines(3)));

        // Swap in a strategy that yields nothing, on a second pipeline sharing the same store.
        var store = provider.GetRequiredService<IVectorStore>();
        var emptyPipeline = new NetIndexPipeline(
            CreateResolver(tenant),
            new EmptyChunkingStrategy(),
            provider.GetRequiredService<IEmbeddingGenerator>(),
            store,
            provider.GetRequiredService<IChatClient>(),
            null);

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => emptyPipeline.IngestAsync(CreateDocument("doc-1", "non-empty content")));
        Assert.IsNotType<NetIndexAuthorizationException>(exception);

        Assert.Equal(3, (await AllChunksAsync(provider)).Count);
    }
}
