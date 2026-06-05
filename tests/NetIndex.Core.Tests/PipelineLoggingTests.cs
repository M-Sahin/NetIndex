#pragma warning disable CS1591
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using NetIndex.Core.Abstractions.Telemetry;
using NetIndex.Core.Logging;
using NSubstitute;
using Xunit;

namespace NetIndex.Core.Tests;

/// <summary>
/// PipelineContract tests that verify structured logging is emitted for all pipeline
/// operations (Story 6.3). Captures IReadOnlyList state so assertions are against
/// structured key/value pairs, not formatted message text (AC-4).
/// </summary>
[Trait("Category", "PipelineContract")]
public sealed class PipelineLoggingTests
{
    // ── Capturing logger ──

    private sealed record LogEntry(
        LogLevel Level,
        EventId EventId,
        Exception? Exception,
        IReadOnlyList<KeyValuePair<string, object?>>? State,
        string Formatted);

    private sealed class CapturingLogger : ILogger<NetIndexPipeline>
    {
        private readonly List<LogEntry> _entries = new();
        public IReadOnlyList<LogEntry> Entries => _entries;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _entries.Add(new LogEntry(
                logLevel,
                eventId,
                exception,
                state as IReadOnlyList<KeyValuePair<string, object?>>,
                formatter(state, exception)));
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }

    // ── Helper: read structured state value ──

    private static object? StateValue(IReadOnlyList<KeyValuePair<string, object?>>? state, string key)
    {
        if (state is null)
        {
            return null;
        }

        foreach (var kv in state)
        {
            if (kv.Key == key)
            {
                return kv.Value;
            }
        }

        return null;
    }

    private static bool StateContainsKey(IReadOnlyList<KeyValuePair<string, object?>>? state, string key)
    {
        if (state is null)
        {
            return false;
        }

        foreach (var kv in state)
        {
            if (kv.Key == key)
            {
                return true;
            }
        }

        return false;
    }

    // ── Mock helpers ──

    private sealed class MockContext
    {
        public ITenantResolver MockResolver { get; } = Substitute.For<ITenantResolver>();
        public IEmbeddingGenerator MockEmbedding { get; } = Substitute.For<IEmbeddingGenerator>();
        public IVectorStore MockStore { get; } = Substitute.For<IVectorStore>();
        public IChatClient MockChat { get; } = Substitute.For<IChatClient>();
    }

    private static (MockContext mocks, CapturingLogger logger, NetIndexPipeline pipeline) BuildPipeline()
    {
        var mocks = new MockContext();
        mocks.MockResolver.ResolveTenantIdAsync(Arg.Any<CancellationToken>())
            .Returns(System.Threading.Tasks.Task.FromResult("test-tenant"));
        mocks.MockEmbedding.Dimensions.Returns(384);
        mocks.MockStore.Dimensions.Returns(384);
        mocks.MockChat.GenerateStreamingAsync(
            Arg.Any<string>(), Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(StubStreamAsync("ok", FinishReason.Stop));

        var logger = new CapturingLogger();
        var pipeline = new NetIndexPipeline(
            mocks.MockResolver, null, mocks.MockEmbedding, mocks.MockStore, mocks.MockChat,
            null, null, logger);
        return (mocks, logger, pipeline);
    }

    private static IDocument CreateDocument(string id, string content)
    {
        var doc = Substitute.For<IDocument>();
        doc.Id.Returns(id);
        doc.Content.Returns(content);
        return doc;
    }

    private static async IAsyncEnumerable<GenerationChunk> StubStreamAsync(
        string text, FinishReason reason,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new GenerationChunk(text, false, reason);
        await System.Threading.Tasks.Task.Yield();
        yield return new GenerationChunk(string.Empty, true, reason);
    }

    private static async IAsyncEnumerable<SearchResult<RagChunk>> StreamOneResultAsync(
        RagChunk chunk, float score,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new SearchResult<RagChunk>(chunk, score, chunk.DocumentId ?? "");
        await System.Threading.Tasks.Task.Yield();
    }

    private static async IAsyncEnumerable<SearchResult<RagChunk>> EmptySearchResultsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await System.Threading.Tasks.Task.Yield();
        yield break;
    }

    private static async IAsyncEnumerable<SearchResult<RagChunk>> ThrowSearchResultsAsync(
        Exception exception,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await System.Threading.Tasks.Task.Yield();
        throw exception;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<GenerationChunk> ThrowGenerationStreamAsync(
        Exception exception,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await System.Threading.Tasks.Task.Yield();
        throw exception;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private sealed class DisposeThrowingGenerationStream : IAsyncEnumerable<GenerationChunk>, IAsyncEnumerator<GenerationChunk>
    {
        public GenerationChunk Current => new(string.Empty, true, FinishReason.Stop);

        public IAsyncEnumerator<GenerationChunk> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

        public System.Threading.Tasks.ValueTask<bool> MoveNextAsync() => System.Threading.Tasks.ValueTask.FromResult(false);

        public System.Threading.Tasks.ValueTask DisposeAsync() => throw new InvalidOperationException("dispose boom");
    }

    // ── AC-1 / AC-2: Ingest success log ──

    [Fact]
    public async System.Threading.Tasks.Task IngestAsync_OnSuccess_EmitsInformationLogWithStructuredFieldsAsync()
    {
        var (mocks, logger, pipeline) = BuildPipeline();

        mocks.MockEmbedding.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(System.Threading.Tasks.Task.FromResult(new float[][] { new float[384] }));
        mocks.MockStore.UpsertAsync(Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(System.Threading.Tasks.Task.CompletedTask);

        await pipeline.IngestAsync(CreateDocument("doc-1", "hello"));

        var entry = Assert.Single(logger.Entries, e => e.EventId == NetIndexLogEventIds.IngestSucceeded);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.NotNull(entry.State);
        Assert.Equal(NetIndexLogOperations.Ingest, StateValue(entry.State, NetIndexLogFields.Operation));
        Assert.Equal(NetIndexLogStatus.Succeeded, StateValue(entry.State, NetIndexLogFields.Status));
        Assert.Equal("test-tenant", StateValue(entry.State, NetIndexSpanTags.TenantId));
        Assert.Equal("doc-1", StateValue(entry.State, NetIndexSpanTags.DocumentId));
        Assert.True((long)StateValue(entry.State, NetIndexLogFields.DurationMs)! >= 0);
        Assert.True(StateContainsKey(entry.State, NetIndexSpanTags.ChunkCount));
        Assert.True(StateContainsKey(entry.State, NetIndexSpanTags.EmbeddingCount));
        Assert.True(StateContainsKey(entry.State, NetIndexSpanTags.EmbeddingDimensions));
    }

    // AC-5: raw document content must not appear in state or formatted message
    [Fact]
    public async System.Threading.Tasks.Task IngestAsync_OnSuccess_DoesNotLogDocumentContentAsync()
    {
        var (mocks, logger, pipeline) = BuildPipeline();

        mocks.MockEmbedding.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(System.Threading.Tasks.Task.FromResult(new float[][] { new float[384] }));
        mocks.MockStore.UpsertAsync(Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(System.Threading.Tasks.Task.CompletedTask);

        await pipeline.IngestAsync(CreateDocument("doc-1", "secret-content-xyz"));

        var entry = Assert.Single(logger.Entries, e => e.EventId == NetIndexLogEventIds.IngestSucceeded);
        Assert.DoesNotContain("secret-content-xyz", entry.Formatted);
        if (entry.State is not null)
        {
            foreach (var kv in entry.State)
            {
                Assert.DoesNotContain("secret-content-xyz", kv.Value?.ToString() ?? string.Empty);
            }
        }
    }

    // ── AC-3: Ingest authorization failure log ──

    [Fact]
    public async System.Threading.Tasks.Task IngestAsync_WhenAuthorizationFails_EmitsErrorLogAsync()
    {
        var (mocks, logger, pipeline) = BuildPipeline();
        mocks.MockResolver.ResolveTenantIdAsync(Arg.Any<CancellationToken>())
            .Returns<System.Threading.Tasks.Task<string>>(_ =>
                throw new NetIndexAuthorizationException("No tenant.", null, null, "MissingTenantIdClaim"));

        await Assert.ThrowsAsync<NetIndexAuthorizationException>(
            () => pipeline.IngestAsync(CreateDocument("doc-1", "text")));

        var entry = Assert.Single(logger.Entries, e => e.EventId == NetIndexLogEventIds.IngestFailed);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.NotNull(entry.State);
        Assert.Equal(NetIndexLogOperations.Ingest, StateValue(entry.State, NetIndexLogFields.Operation));
        Assert.Equal(NetIndexLogStatus.Failed, StateValue(entry.State, NetIndexLogFields.Status));
        Assert.Equal("NetIndex.Core.Abstractions.NetIndexAuthorizationException",
            StateValue(entry.State, NetIndexLogFields.ExceptionType));
        Assert.Equal("MissingTenantIdClaim", StateValue(entry.State, NetIndexLogFields.FailureReason));
    }

    // ── AC-3: cancellation logs at Information ──

    [Fact]
    public async System.Threading.Tasks.Task IngestAsync_WhenCanceled_EmitsInformationLogWithCanceledStatusAsync()
    {
        var (mocks, logger, pipeline) = BuildPipeline();
        mocks.MockEmbedding.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(System.Threading.Tasks.Task.FromResult(new float[][] { new float[384] }));
        mocks.MockStore.UpsertAsync(Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns<System.Threading.Tasks.Task>(_ => throw new OperationCanceledException("canceled"));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => pipeline.IngestAsync(CreateDocument("doc-1", "text")));

        var entry = Assert.Single(logger.Entries, e => e.EventId == NetIndexLogEventIds.IngestFailed);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(NetIndexLogStatus.Canceled, StateValue(entry.State, NetIndexLogFields.Status));
    }

    [Fact]
    public async System.Threading.Tasks.Task IngestAsync_WhenGenericFailureIsWrapped_LogsProviderErrorCodeAsync()
    {
        var (mocks, logger, pipeline) = BuildPipeline();
        mocks.MockEmbedding.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(System.Threading.Tasks.Task.FromResult(new float[][] { new float[384] }));
        mocks.MockStore.UpsertAsync(Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns<System.Threading.Tasks.Task>(_ => throw new InvalidOperationException("raw upsert failure"));

        await Assert.ThrowsAsync<NetIndexProviderException>(
            () => pipeline.IngestAsync(CreateDocument("doc-1", "text")));

        var entry = Assert.Single(logger.Entries, e => e.EventId == NetIndexLogEventIds.IngestFailed);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<NetIndexProviderException>(entry.Exception);
        Assert.Equal("NetIndex.Core.Abstractions.NetIndexProviderException",
            StateValue(entry.State, NetIndexLogFields.ExceptionType));
        Assert.Equal("IngestionFailed", StateValue(entry.State, NetIndexLogFields.ErrorCode));
    }

    // ── AC-2: Query success log ──

    [Fact]
    public async System.Threading.Tasks.Task QueryAsync_OnSuccess_EmitsInformationLogWithRetrieveFieldsAsync()
    {
        var (mocks, logger, pipeline) = BuildPipeline();

        var tenantMeta = new Dictionary<string, string> { [RagChunkMetadata.TenantId] = "test-tenant" };
        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(System.Threading.Tasks.Task.FromResult(new float[384]));
        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(StreamOneResultAsync(
                new RagChunk("c1", "txt", new float[384], "doc-1", tenantMeta), 0.9f));

        await foreach (var _ in pipeline.QueryAsync("q")) { }

        var entry = Assert.Single(logger.Entries, e => e.EventId == NetIndexLogEventIds.QuerySucceeded);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.NotNull(entry.State);
        Assert.Equal(NetIndexLogOperations.Query, StateValue(entry.State, NetIndexLogFields.Operation));
        Assert.Equal(NetIndexLogStatus.Succeeded, StateValue(entry.State, NetIndexLogFields.Status));
        Assert.Equal("test-tenant", StateValue(entry.State, NetIndexSpanTags.TenantId));
        Assert.Equal(384, StateValue(entry.State, NetIndexSpanTags.EmbeddingDimensions));
        Assert.True(StateContainsKey(entry.State, NetIndexSpanTags.RetrieveTop));
        Assert.True(StateContainsKey(entry.State, NetIndexSpanTags.RetrieveResultCount));
        Assert.True(StateContainsKey(entry.State, NetIndexSpanTags.RetrieveFilteredCount));
    }

    // AC-5: raw query text must not appear in state or formatted message
    [Fact]
    public async System.Threading.Tasks.Task QueryAsync_OnSuccess_DoesNotLogRawQueryTextAsync()
    {
        var (mocks, logger, pipeline) = BuildPipeline();

        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(System.Threading.Tasks.Task.FromResult(new float[384]));
        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptySearchResultsAsync());

        await foreach (var _ in pipeline.QueryAsync("secret-query-text")) { }

        var entry = Assert.Single(logger.Entries, e => e.EventId == NetIndexLogEventIds.QuerySucceeded);
        Assert.DoesNotContain("secret-query-text", entry.Formatted);
        if (entry.State is not null)
        {
            foreach (var kv in entry.State)
            {
                Assert.DoesNotContain("secret-query-text", kv.Value?.ToString() ?? string.Empty);
            }
        }
    }

    // ── AC-3: Query vector-store failure log ──

    [Fact]
    public async System.Threading.Tasks.Task QueryAsync_WhenVectorStoreFails_EmitsErrorLogAsync()
    {
        var (mocks, logger, pipeline) = BuildPipeline();

        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(System.Threading.Tasks.Task.FromResult(new float[384]));
        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ThrowSearchResultsAsync(
                new NetIndexStorageException("boom", "InMemoryVectorStore", "Query", null)));

        await Assert.ThrowsAsync<NetIndexStorageException>(
            async () => { await foreach (var _ in pipeline.QueryAsync("q")) { } });

        var entry = Assert.Single(logger.Entries, e => e.EventId == NetIndexLogEventIds.QueryFailed);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.NotNull(entry.State);
        Assert.Equal(NetIndexLogOperations.Query, StateValue(entry.State, NetIndexLogFields.Operation));
        Assert.Equal(NetIndexLogStatus.Failed, StateValue(entry.State, NetIndexLogFields.Status));
        Assert.Equal("InMemoryVectorStore", StateValue(entry.State, NetIndexLogFields.StoreName));
        Assert.Equal("Query", StateValue(entry.State, NetIndexLogFields.StorageOperation));
    }

    // ── AC-2: Generate success log ──

    [Fact]
    public async System.Threading.Tasks.Task GenerateAsync_OnSuccess_EmitsInformationLogWithContextChunkCountAsync()
    {
        var (mocks, logger, pipeline) = BuildPipeline();

        var tenantMeta = new Dictionary<string, string> { [RagChunkMetadata.TenantId] = "test-tenant" };
        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(System.Threading.Tasks.Task.FromResult(new float[384]));
        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(StreamOneResultAsync(
                new RagChunk("c1", "ctx", new float[384], "doc-1", tenantMeta), 0.9f));
        mocks.MockChat.GenerateStreamingAsync(
            Arg.Any<string>(), Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(StubStreamAsync("answer", FinishReason.Stop));

        await foreach (var _ in pipeline.GenerateAsync("q")) { }

        var entry = Assert.Single(logger.Entries, e => e.EventId == NetIndexLogEventIds.GenerateSucceeded);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.NotNull(entry.State);
        Assert.Equal(NetIndexLogOperations.Generate, StateValue(entry.State, NetIndexLogFields.Operation));
        Assert.Equal(NetIndexLogStatus.Succeeded, StateValue(entry.State, NetIndexLogFields.Status));
        Assert.Equal("test-tenant", StateValue(entry.State, NetIndexSpanTags.TenantId));
        Assert.Equal(384, StateValue(entry.State, NetIndexSpanTags.EmbeddingDimensions));
        Assert.True(StateContainsKey(entry.State, NetIndexSpanTags.RetrieveTop));
        Assert.Equal(1, StateValue(entry.State, NetIndexSpanTags.RetrieveResultCount));
        Assert.Equal(1, StateValue(entry.State, NetIndexSpanTags.RetrieveFilteredCount));
        Assert.True(StateContainsKey(entry.State, NetIndexSpanTags.ContextChunkCount));
    }

    // AC-5: generated answer text must not appear in state or formatted message
    [Fact]
    public async System.Threading.Tasks.Task GenerateAsync_OnSuccess_DoesNotLogGeneratedAnswerTextAsync()
    {
        var (mocks, logger, pipeline) = BuildPipeline();

        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(System.Threading.Tasks.Task.FromResult(new float[384]));
        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptySearchResultsAsync());
        mocks.MockChat.GenerateStreamingAsync(
            Arg.Any<string>(), Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(StubStreamAsync("secret-answer-xyz", FinishReason.Stop));

        await foreach (var _ in pipeline.GenerateAsync("q")) { }

        var entry = Assert.Single(logger.Entries, e => e.EventId == NetIndexLogEventIds.GenerateSucceeded);
        Assert.DoesNotContain("secret-answer-xyz", entry.Formatted);
        if (entry.State is not null)
        {
            foreach (var kv in entry.State)
            {
                Assert.DoesNotContain("secret-answer-xyz", kv.Value?.ToString() ?? string.Empty);
            }
        }
    }

    // ── AC-3: Generate chat-stream failure log ──

    [Fact]
    public async System.Threading.Tasks.Task GenerateAsync_WhenChatStreamFails_EmitsErrorLogAsync()
    {
        var (mocks, logger, pipeline) = BuildPipeline();

        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(System.Threading.Tasks.Task.FromResult(new float[384]));
        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptySearchResultsAsync());
        mocks.MockChat.GenerateStreamingAsync(
            Arg.Any<string>(), Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(ThrowGenerationStreamAsync(new InvalidOperationException("chat boom")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => { await foreach (var _ in pipeline.GenerateAsync("q")) { } });

        var entry = Assert.Single(logger.Entries, e => e.EventId == NetIndexLogEventIds.GenerateFailed);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.NotNull(entry.State);
        Assert.Equal(NetIndexLogOperations.Generate, StateValue(entry.State, NetIndexLogFields.Operation));
        Assert.Equal(NetIndexLogStatus.Failed, StateValue(entry.State, NetIndexLogFields.Status));
        Assert.Equal("System.InvalidOperationException",
            StateValue(entry.State, NetIndexLogFields.ExceptionType));
        Assert.Equal("chat boom", StateValue(entry.State, NetIndexLogFields.ExceptionMessage));
    }

    [Fact]
    public async System.Threading.Tasks.Task GenerateAsync_WhenChatStreamSetupFails_EmitsErrorLogAsync()
    {
        var (mocks, logger, pipeline) = BuildPipeline();

        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(System.Threading.Tasks.Task.FromResult(new float[384]));
        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptySearchResultsAsync());
        mocks.MockChat.GenerateStreamingAsync(
            Arg.Any<string>(), Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns<IAsyncEnumerable<GenerationChunk>>(_ => throw new InvalidOperationException("setup boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => { await foreach (var _ in pipeline.GenerateAsync("q")) { } });

        var entry = Assert.Single(logger.Entries, e => e.EventId == NetIndexLogEventIds.GenerateFailed);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(NetIndexLogStatus.Failed, StateValue(entry.State, NetIndexLogFields.Status));
        Assert.Equal("setup boom", StateValue(entry.State, NetIndexLogFields.ExceptionMessage));
    }

    [Fact]
    public async System.Threading.Tasks.Task GenerateAsync_WhenChatStreamDisposeFails_EmitsErrorLogAsync()
    {
        var (mocks, logger, pipeline) = BuildPipeline();

        mocks.MockEmbedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(System.Threading.Tasks.Task.FromResult(new float[384]));
        mocks.MockStore.QueryAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptySearchResultsAsync());
        mocks.MockChat.GenerateStreamingAsync(
            Arg.Any<string>(), Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(new DisposeThrowingGenerationStream());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => { await foreach (var _ in pipeline.GenerateAsync("q")) { } });

        var entry = Assert.Single(logger.Entries, e => e.EventId == NetIndexLogEventIds.GenerateFailed);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(NetIndexLogStatus.Failed, StateValue(entry.State, NetIndexLogFields.Status));
        Assert.Equal("dispose boom", StateValue(entry.State, NetIndexLogFields.ExceptionMessage));
    }

    // ── AC-3: Generate auth failure log ──

    [Fact]
    public async System.Threading.Tasks.Task GenerateAsync_WhenAuthorizationFails_EmitsErrorLogAsync()
    {
        var (mocks, logger, pipeline) = BuildPipeline();
        mocks.MockResolver.ResolveTenantIdAsync(Arg.Any<CancellationToken>())
            .Returns<System.Threading.Tasks.Task<string>>(_ =>
                throw new NetIndexAuthorizationException("Denied.", null, null, "NoTenantResolverConfigured"));

        await Assert.ThrowsAsync<NetIndexAuthorizationException>(
            async () => { await foreach (var _ in pipeline.GenerateAsync("q")) { } });

        var entry = Assert.Single(logger.Entries, e => e.EventId == NetIndexLogEventIds.GenerateFailed);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(NetIndexLogStatus.Failed, StateValue(entry.State, NetIndexLogFields.Status));
        Assert.Equal("NoTenantResolverConfigured", StateValue(entry.State, NetIndexLogFields.FailureReason));
    }

    // ── AC-1: DI regression — AddNetIndex().Build() wires logger, singleton is shared ──

    [Fact]
    public async System.Threading.Tasks.Task AddNetIndex_Build_ResolvesINetIndexPipelineAndNetIndexPipelineAsSameSingletonAsync()
    {
        var services = new ServiceCollection();

        var resolver = Substitute.For<ITenantResolver>();
        var embedding = Substitute.For<IEmbeddingGenerator>();
        var store = Substitute.For<IVectorStore>();
        var chat = Substitute.For<IChatClient>();
        var logger = new CapturingLogger();
        resolver.ResolveTenantIdAsync(Arg.Any<CancellationToken>())
            .Returns(System.Threading.Tasks.Task.FromResult("test-tenant"));
        embedding.Dimensions.Returns(384);
        embedding.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(System.Threading.Tasks.Task.FromResult(new float[][] { new float[384] }));
        store.Dimensions.Returns(384);
        store.UpsertAsync(Arg.Any<IEnumerable<RagChunk>>(), Arg.Any<CancellationToken>())
            .Returns(System.Threading.Tasks.Task.CompletedTask);

        services.AddSingleton(resolver);
        services.AddSingleton(embedding);
        services.AddSingleton(store);
        services.AddSingleton(chat);
        services.AddSingleton<ILogger<NetIndexPipeline>>(logger);
        services.AddNetIndex().Build();

        var provider = services.BuildServiceProvider();
        var pipeline1 = provider.GetRequiredService<INetIndexPipeline>();
        var pipeline2 = provider.GetRequiredService<NetIndexPipeline>();

        Assert.Same(pipeline1, pipeline2);
        Assert.IsType<NetIndexPipeline>(pipeline1);

        await pipeline1.IngestAsync(CreateDocument("doc-di", "text"));

        Assert.Single(logger.Entries, e => e.EventId == NetIndexLogEventIds.IngestSucceeded);
    }
}
