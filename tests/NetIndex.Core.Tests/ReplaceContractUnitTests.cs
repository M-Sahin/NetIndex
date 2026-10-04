#pragma warning disable CS1591
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using NSubstitute;
using Xunit;

namespace NetIndex.Core.Tests;

/// <summary>
/// Unit tests (run on PRs) for the fail-closed default of <see cref="IVectorStore.ReplaceDocumentAsync"/>
/// and for what the pipeline hands to the chunking strategy and the store.
/// </summary>
public sealed class ReplaceContractUnitTests
{
    private sealed class MinimalStore : IVectorStore
    {
        public int Dimensions => 3;

        public int Writes { get; private set; }

        public Task UpsertAsync(IEnumerable<RagChunk> chunks, CancellationToken cancellationToken = default)
        {
            Writes++;
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<SearchResult<RagChunk>> QueryAsync(
            float[] queryVector, int top = 5, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task DeleteAsync(string documentId, CancellationToken cancellationToken = default)
        {
            Writes++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task DefaultReplace_FailsClosed_AndWritesAndDeletesNothingAsync()
    {
        var store = new MinimalStore();
        IVectorStore asInterface = store;
        var chunk = new RagChunk("d_chunk_0", "t", new float[3], "d", null);

        var exception = await Assert.ThrowsAsync<NetIndexConfigurationException>(
            () => asInterface.ReplaceDocumentAsync("d", new[] { chunk }));

        Assert.Contains("ReplaceDocumentAsync", exception.Message);
        Assert.Equal(0, store.Writes);
    }

    private static (NetIndexPipeline Pipeline, IChunkingStrategy Strategy, IVectorStore Store) CreatePipeline(ChunkingOptions options)
    {
        var resolver = Substitute.For<ITenantResolver>();
        resolver.ResolveTenantIdAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult("tenant-a"));

        var strategy = Substitute.For<IChunkingStrategy>();
        strategy.ChunkAsync(Arg.Any<string>(), Arg.Any<ChunkingOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<RagChunk>>(new[] { new RagChunk("chunk_0", "hello", null, "pending", null) }));

        var embedder = Substitute.For<IEmbeddingGenerator>();
        embedder.Dimensions.Returns(3);
        embedder.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new[] { new float[3] }));

        var store = Substitute.For<IVectorStore>();
        store.Dimensions.Returns(3);
        store.ReplaceDocumentAsync(Arg.Any<string>(), Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var pipeline = new NetIndexPipeline(
            resolver, strategy, embedder, store, Substitute.For<IChatClient>(), null, null, null, options);
        return (pipeline, strategy, store);
    }

    private static IDocument Doc(string id, string content)
    {
        var doc = Substitute.For<IDocument>();
        doc.Id.Returns(id);
        doc.Content.Returns(content);
        return doc;
    }

    [Fact]
    public async Task Ingest_PassesTheExactConfiguredChunkingOptionsInstanceAsync()
    {
        var options = new ChunkingOptions(200, 20, "\n");
        var (pipeline, strategy, _) = CreatePipeline(options);

        await pipeline.IngestAsync(Doc("doc-1", "hello"));

        await strategy.Received(1).ChunkAsync(
            "hello",
            Arg.Is<ChunkingOptions>(o => ReferenceEquals(o, options)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ingest_CallsReplaceDocumentWithTheDocumentIdAsync()
    {
        var (pipeline, _, store) = CreatePipeline(new ChunkingOptions(200, 20, "\n"));

        await pipeline.IngestAsync(Doc("doc-1", "hello"));

        await store.Received(1).ReplaceDocumentAsync(
            "doc-1",
            Arg.Is<IEnumerable<RagChunk>>(c => c.Single().Id == "doc-1_chunk_0"),
            Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
    }

    [Fact]
    public async Task Ingest_WhitespaceOnlyChunks_AreFilteredAndRejectedAsync()
    {
        var (pipeline, strategy, store) = CreatePipeline(new ChunkingOptions(200, 20, "\n"));
        strategy.ChunkAsync(Arg.Any<string>(), Arg.Any<ChunkingOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<RagChunk>>(new[] { new RagChunk("chunk_0", "  \n ", null, "pending", null) }));

        await Assert.ThrowsAsync<ArgumentException>(() => pipeline.IngestAsync(Doc("doc-1", "hello")));

        await store.DidNotReceiveWithAnyArgs().ReplaceDocumentAsync(default!, default!, default);
    }

    [Fact]
    public async Task Ingest_BlankDocumentId_IsRejectedBeforeAnyStoreCallAsync()
    {
        var (pipeline, _, store) = CreatePipeline(new ChunkingOptions(200, 20, "\n"));

        await Assert.ThrowsAsync<ArgumentException>(() => pipeline.IngestAsync(Doc(" ", "hello")));

        await store.DidNotReceiveWithAnyArgs().ReplaceDocumentAsync(default!, default!, default);
    }
}
