#pragma warning disable CS1591
using Microsoft.Extensions.DependencyInjection;
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion;
using NetIndex.Testing.Common;
using NSubstitute;

namespace NetIndex.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class LocalPipelineEndToEndTests
{
    // ── Helpers ──

    private static ITenantResolver CreateAllowAllResolver()
    {
        var resolver = Substitute.For<ITenantResolver>();
        resolver.ResolveTenantIdAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("test-tenant"));
        resolver.ResolveClaimsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string> { { "tenant", "test-tenant" } }));
        return resolver;
    }

    private static IDocument CreateDocument(string id, string content)
    {
        var doc = Substitute.For<IDocument>();
        doc.Id.Returns(id);
        doc.Content.Returns(content);
        doc.Metadata.Returns((IReadOnlyDictionary<string, string>?)null);
        doc.SourceUri.Returns((Uri?)null);
        return doc;
    }

    private static async Task<List<SearchResult<RagChunk>>> CollectResultsAsync(
        IAsyncEnumerable<SearchResult<RagChunk>> source)
    {
        var results = new List<SearchResult<RagChunk>>();
        await foreach (var result in source)
        {
            results.Add(result);
        }
        return results;
    }

    // ── AC #2: IngestAsync → Chunk → Embed → Store ──

    [Fact]
    public async Task FullPipeline_IngestAndQuery_ReturnsResultsAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(CreateAllowAllResolver());
        services.AddSingleton<IEmbeddingGenerator>(new FakeEmbeddingGenerator(384));
        services.AddNetIndex(builder => builder.UseChunking(o => o.FixedSize(512, 64))).Build();

        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<INetIndexPipeline>();

        var document = CreateDocument("doc-e2e-1",
            "NetIndex is a RAG framework for .NET. It provides document ingestion, " +
            "vector search, and LLM-powered generation through a fluent builder API. " +
            "Developers can use it to build intelligent search and question-answering systems " +
            "that work entirely locally without cloud dependencies.");

        // Act
        await pipeline.IngestAsync(document);
        var results = await CollectResultsAsync(pipeline.QueryAsync("RAG framework for .NET"));

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r =>
            r.Item.DocumentId == "doc-e2e-1" && r.Score > 0);
    }

    [Fact]
    public async Task FullPipeline_IngestAndQuery_WithChunking_SplitsContentAsync()
    {
        // Arrange — FixedSizeChunkingStrategy with the pipeline's default 1000-token (4000-char) limit.
        // Three paragraphs: the first two fit within 4000 chars; adding the third exceeds it → 2 chunks.
        var services = new ServiceCollection();
        services.AddSingleton(CreateAllowAllResolver());
        services.AddSingleton<IEmbeddingGenerator>(new FakeEmbeddingGenerator(384));
        services.AddNetIndex(builder => builder.UseChunking(o => o.FixedSize(512, 64))).Build();

        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<INetIndexPipeline>();

        var para1 = string.Concat(Enumerable.Repeat("NetIndex provides RAG capabilities for .NET. ", 34));    // ~1530 chars
        var para2 = string.Concat(Enumerable.Repeat("Vector search retrieves the most relevant chunks. ", 31)); // ~1550 chars
        var para3 = string.Concat(Enumerable.Repeat("The pipeline orchestrates ingest and query flows. ", 31)); // ~1550 chars
        var document = CreateDocument("doc-chunked", $"{para1}\n\n{para2}\n\n{para3}");

        // Act
        await pipeline.IngestAsync(document);
        var results = await CollectResultsAsync(pipeline.QueryAsync("RAG pipeline chunking"));

        // Assert — document was split into multiple chunks, all referencing the same document ID
        Assert.True(results.Count > 1, $"Expected multiple chunks from splitting, got {results.Count}");
        Assert.All(results, r => Assert.Equal("doc-chunked", r.Item.DocumentId));
    }

    [Fact]
    public async Task FullPipeline_IngestMultipleDocuments_QueryReturnsRelevantAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(CreateAllowAllResolver());
        services.AddSingleton<IEmbeddingGenerator>(new FakeEmbeddingGenerator(384));
        services.AddNetIndex(builder => builder.UseChunking(o => o.FixedSize(512, 64))).Build();

        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<INetIndexPipeline>();

        var docA = CreateDocument("doc-a",
            "Machine learning models require large amounts of training data to perform well. " +
            "Supervised learning uses labeled datasets to train algorithms that can classify data " +
            "or predict outcomes accurately.");
        var docB = CreateDocument("doc-b",
            "NetIndex is an open-source RAG framework for the .NET ecosystem. It provides " +
            "document ingestion, vector search, and streaming LLM generation out of the box.");

        // Act
        await pipeline.IngestAsync(docA);
        await pipeline.IngestAsync(docB);

        var results = await CollectResultsAsync(pipeline.QueryAsync("What is NetIndex RAG framework?"));

        // Assert — doc-b should be more relevant to the query
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Item.DocumentId == "doc-b");
    }

    // ── AC #3: QueryAsync returns ordered results with scores ──

    [Fact]
    public async Task FullPipeline_Query_ReturnsOrderedByScoreAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(CreateAllowAllResolver());
        services.AddSingleton<IEmbeddingGenerator>(new FakeEmbeddingGenerator(384));
        services.AddNetIndex(builder => builder.UseChunking(o => o.FixedSize(512, 64))).Build();

        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<INetIndexPipeline>();

        var document = CreateDocument("doc-scores",
            "NetIndex provides RAG capabilities for .NET developers. " +
            "It supports chunking, embedding, vector search, and LLM generation. " +
            "The pipeline is fully configurable through a fluent builder API.");

        await pipeline.IngestAsync(document);

        // Act
        var results = await CollectResultsAsync(pipeline.QueryAsync("RAG"));

        // Assert
        Assert.NotEmpty(results);
        for (var i = 1; i < results.Count; i++)
        {
            Assert.True(results[i - 1].Score >= results[i].Score,
                $"Results not ordered by descending score at index {i}: " +
                $"{results[i - 1].Score} < {results[i].Score}");
        }
    }

    // ── AC #4: GenerateAsync streams tokens ──

    [Fact]
    public async Task FullPipeline_GenerateAsync_ReturnsStreamingTokensAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(CreateAllowAllResolver());
        services.AddSingleton<IEmbeddingGenerator>(new FakeEmbeddingGenerator(384));
        services.AddNetIndex(builder => builder.UseChunking(o => o.FixedSize(512, 64))).Build();

        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<INetIndexPipeline>();

        var document = CreateDocument("doc-gen",
            "NetIndex enables RAG-powered question answering. " +
            "It retrieves relevant chunks from a vector store and passes them " +
            "to an LLM for answer synthesis with citations.");

        await pipeline.IngestAsync(document);

        // Act
        var chunks = new List<GenerationChunk>();
        await foreach (var chunk in pipeline.GenerateAsync("How does RAG work?"))
        {
            chunks.Add(chunk);
        }

        // Assert
        Assert.NotEmpty(chunks);
        Assert.True(chunks[^1].IsComplete);
    }

    // ── AC #2: Ingest → Delete → Query returns empty ──

    [Fact]
    public async Task FullPipeline_IngestThenDeleteThenQuery_ReturnsEmptyAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(CreateAllowAllResolver());
        services.AddSingleton<IEmbeddingGenerator>(new FakeEmbeddingGenerator(384));
        services.AddNetIndex(builder => builder.UseChunking(o => o.FixedSize(512, 64))).Build();

        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<INetIndexPipeline>();

        var document = CreateDocument("doc-del",
            "This document will be deleted after ingestion. " +
            "It contains information about temporary data that should not persist.");

        await pipeline.IngestAsync(document);
        var beforeDelete = await CollectResultsAsync(pipeline.QueryAsync("temporary data"));
        Assert.NotEmpty(beforeDelete);

        // Act — delete by document ID via vector store
        var store = provider.GetRequiredService<IVectorStore>();
        await store.DeleteAsync("doc-del", CancellationToken.None);

        var afterDelete = await CollectResultsAsync(pipeline.QueryAsync("temporary data"));

        // Assert
        Assert.DoesNotContain(afterDelete, r => r.Item.DocumentId == "doc-del");
    }

    // ── AC #2: Deny-all auth enforcement ──

    [Fact]
    public async Task FullPipeline_WithoutAuth_ThrowsAuthorizationExceptionAsync()
    {
        // Arrange — no ITenantResolver registered, so DenyAllTenantResolver is used
        var services = new ServiceCollection();
        services.AddSingleton<IEmbeddingGenerator>(new FakeEmbeddingGenerator(384));
        services.AddNetIndex(builder => builder.UseChunking(o => o.FixedSize(512, 64))).Build();

        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<INetIndexPipeline>();
        var document = CreateDocument("doc-noauth", "test content");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<NetIndexAuthorizationException>(
            () => pipeline.IngestAsync(document));

        Assert.Equal("No ITenantResolver configured. Access denied by default.", exception.Message);
    }

    // ── AC #1: Build succeeds with correct configuration ──

    [Fact]
    public void FullPipeline_Build_WithValidConfiguration_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateAllowAllResolver());
        services.AddSingleton<IEmbeddingGenerator>(new FakeEmbeddingGenerator(384));

        var builder = services.AddNetIndex(builder => builder.UseChunking(o => o.FixedSize(512, 64)));
        var ex = Record.Exception(() => builder.Build());

        Assert.Null(ex);
    }

    // ── Task 6 / AC #7: Dimension mismatch fails at Build time ──

    [Fact]
    [Trait("Category", "PipelineContract")]
    public void FullPipeline_DimensionMismatch_FailsAtBuildTime()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(CreateAllowAllResolver());
        // FakeEmbeddingGenerator with 4 dimensions vs default InMemoryVectorStore with 384
        services.AddSingleton<IEmbeddingGenerator>(new FakeEmbeddingGenerator(4));
        var builder = services.AddNetIndex(builder => builder.UseChunking(o => o.FixedSize(512, 64)));

        // Act & Assert — Build() throws because dimension validation occurs during registration
        var exception = Assert.Throws<NetIndexConfigurationException>(() => builder.Build());

        Assert.Contains("dimension mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── AC #1: Default AddNetIndex (no configuration) wires successfully ──

    [Fact]
    public void FullPipeline_DefaultConfiguration_WiresSuccessfully()
    {
        // Arrange — no explicit configuration beyond AddNetIndex
        var services = new ServiceCollection();
        services.AddSingleton(CreateAllowAllResolver());
        services.AddNetIndex().Build();

        using var provider = services.BuildServiceProvider();

        // Act
        var pipeline = provider.GetRequiredService<INetIndexPipeline>();
        var store = provider.GetRequiredService<IVectorStore>();
        var embedder = provider.GetRequiredService<IEmbeddingGenerator>();
        var chat = provider.GetRequiredService<IChatClient>();

        // Assert
        Assert.NotNull(pipeline);
        Assert.Equal(384, store.Dimensions);
        Assert.Equal(384, embedder.Dimensions);
        Assert.NotNull(chat);
    }
}