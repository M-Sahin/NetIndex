#pragma warning disable CS1591
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using NetIndex.Core.Abstractions.Telemetry;
using NetIndex.Core.Logging;
using NSubstitute;
using Xunit;

namespace NetIndex.Core.Tests;

/// <summary>
/// Tenant-safe removal through <see cref="INetIndexPipeline.DeleteAsync"/> (Story 2.14): authorize first,
/// blank id before any store call, the store receives the resolved tenant, span and log signals.
/// </summary>
[Trait("Category", "PipelineContract")]
[Collection(ActivityListenerCollection.NAME)]
public sealed class PipelineDeleteTests : IDisposable
{
    private readonly ConcurrentBag<Activity> _stopped = new();
    private readonly ActivityListener _listener;

    public PipelineDeleteTests()
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

    private sealed record LogEntry(LogLevel Level, EventId EventId, Exception? Exception, IReadOnlyList<KeyValuePair<string, object?>>? State);

    private static object? StateValue(LogEntry entry, string key) =>
        entry.State?.FirstOrDefault(kv => kv.Key == key).Value;

    private sealed class CapturingLogger : ILogger<NetIndexPipeline>
    {
        public List<LogEntry> Entries { get; } = new();

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, eventId, exception, state as IReadOnlyList<KeyValuePair<string, object?>>));

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }

    private static (NetIndexPipeline Pipeline, IVectorStore Store, CapturingLogger Logger) Create(
        Func<CancellationToken, Task<string>> resolve, IVectorStore? store = null)
    {
        var resolver = Substitute.For<ITenantResolver>();
        resolver.ResolveTenantIdAsync(Arg.Any<CancellationToken>()).Returns(call => resolve(call.Arg<CancellationToken>()));

        var embedder = Substitute.For<IEmbeddingGenerator>();
        embedder.Dimensions.Returns(3);
        store ??= Substitute.For<IVectorStore>();
        var logger = new CapturingLogger();
        var pipeline = new NetIndexPipeline(
            resolver, null, embedder, store, Substitute.For<IChatClient>(), null, null, logger, null);
        return (pipeline, store, logger);
    }

    [Fact]
    public async Task DeleteAsync_PassesTheResolvedTenantToTheStore_AndLogsSuccessAsync()
    {
        var (pipeline, store, logger) = Create(_ => Task.FromResult("tenant-a"));
        store.DeleteDocumentAsync("d1", "tenant-a", Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await pipeline.DeleteAsync("d1");

        await store.Received(1).DeleteDocumentAsync("d1", "tenant-a", Arg.Any<CancellationToken>());
        await store.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(1004, entry.EventId.Id);
        Assert.Equal("DeleteSucceeded", entry.EventId.Name);
        Assert.Equal("delete", StateValue(entry, NetIndexLogFields.Operation));
        Assert.Equal(NetIndexLogStatus.Succeeded, StateValue(entry, NetIndexLogFields.Status));
        Assert.Equal("tenant-a", StateValue(entry, NetIndexSpanTags.TenantId));
        Assert.Equal("d1", StateValue(entry, NetIndexSpanTags.DocumentId));
        Assert.NotNull(StateValue(entry, NetIndexLogFields.DurationMs));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DeleteAsync_BlankTenant_IsDeniedAsAuthorization_AndNeverCallsTheStoreAsync(string blankTenant)
    {
        var (pipeline, store, logger) = Create(_ => Task.FromResult(blankTenant));

        var exception = await Assert.ThrowsAsync<NetIndexAuthorizationException>(() => pipeline.DeleteAsync("d1"));

        Assert.Equal("BlankTenantId", exception.FailureReason);
        await store.DidNotReceiveWithAnyArgs().DeleteDocumentAsync(default!, default!, default);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(1104, entry.EventId.Id);
        Assert.Null(StateValue(entry, NetIndexSpanTags.TenantId));
    }

    [Fact]
    public async Task DeleteAsync_WhenCanceled_RethrowsUnwrapped_AndLogsCanceledAtInformationAsync()
    {
        var (pipeline, store, logger) = Create(_ => Task.FromResult("tenant-a"));
        store.DeleteDocumentAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new OperationCanceledException("canceled"));

        await Assert.ThrowsAsync<OperationCanceledException>(() => pipeline.DeleteAsync("d1"));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(1104, entry.EventId.Id);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(NetIndexLogStatus.Canceled, StateValue(entry, NetIndexLogFields.Status));
    }

    [Fact]
    public async Task DeleteAsync_NullTenant_ThrowsAuthorization_AndNeverCallsTheStoreAsync()
    {
        using var root = new Activity("test.root").Start();
        var (pipeline, store, logger) = Create(_ => Task.FromResult<string>(null!));

        var exception = await Assert.ThrowsAsync<NetIndexAuthorizationException>(() => pipeline.DeleteAsync("d1"));

        Assert.Equal("NullTenantId", exception.FailureReason);
        await store.DidNotReceiveWithAnyArgs().DeleteDocumentAsync(default!, default!, default);
        await store.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(1104, entry.EventId.Id);
        Assert.Null(StateValue(entry, NetIndexSpanTags.TenantId));
        var span = _stopped.Single(a => a.TraceId == root.TraceId && a.OperationName == NetIndexSpanNames.Delete);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    [Fact]
    public async Task DeleteAsync_ThrowingResolver_IsDeniedAndNeverCallsTheStoreAsync()
    {
        var (pipeline, store, _) = Create(_ => throw new InvalidOperationException("boom"));

        var exception = await Assert.ThrowsAsync<NetIndexAuthorizationException>(() => pipeline.DeleteAsync("d1"));

        Assert.Equal("TenantResolutionFailed", exception.FailureReason);
        await store.DidNotReceiveWithAnyArgs().DeleteDocumentAsync(default!, default!, default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DeleteAsync_BlankId_ThrowsArgumentException_AfterAuthorize_BeforeTheStoreAsync(string blank)
    {
        var authorized = false;
        var (pipeline, store, logger) = Create(_ =>
        {
            authorized = true;
            return Task.FromResult("tenant-a");
        });

        var exception = await Record.ExceptionAsync(() => pipeline.DeleteAsync(blank));

        Assert.IsType<ArgumentException>(exception);
        Assert.True(authorized);
        await store.DidNotReceiveWithAnyArgs().DeleteDocumentAsync(default!, default!, default);
        Assert.Equal(1104, Assert.Single(logger.Entries).EventId.Id);
    }

    [Fact]
    public Task DeleteAsync_BlankId_WithDeniedTenant_ThrowsAuthorizationFirstAsync()
    {
        var (pipeline, _, _) = Create(_ => Task.FromResult<string>(null!));

        return Assert.ThrowsAsync<NetIndexAuthorizationException>(() => pipeline.DeleteAsync(" "));
    }

    [Fact]
    public async Task DeleteAsync_StoreWithoutOverride_FailsClosedWithConfigurationExceptionAsync()
    {
        var (pipeline, _, logger) = Create(_ => Task.FromResult("tenant-a"), new NoDeleteStore());

        await Assert.ThrowsAsync<NetIndexConfigurationException>(() => pipeline.DeleteAsync("d1"));

        Assert.Equal(1104, Assert.Single(logger.Entries).EventId.Id);
    }

    [Fact]
    public async Task DeleteAsync_UnexpectedStoreFailure_IsWrappedAsProviderExceptionAsync()
    {
        var (pipeline, store, _) = Create(_ => Task.FromResult("tenant-a"));
        store.DeleteDocumentAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("db down")));

        var exception = await Assert.ThrowsAsync<NetIndexProviderException>(() => pipeline.DeleteAsync("d1"));

        Assert.Equal("DeleteFailed", exception.ErrorCode);
    }

    [Fact]
    public async Task DeleteAsync_EmitsADeleteSpanWithDocumentAndTenantTagsAsync()
    {
        using var root = new Activity("test.root").Start();
        var (pipeline, store, _) = Create(_ => Task.FromResult("tenant-a"));
        store.DeleteDocumentAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await pipeline.DeleteAsync("d1");

        var span = _stopped.Single(a => a.TraceId == root.TraceId && a.OperationName == "netindex.delete");
        Assert.Equal("d1", span.GetTagItem(NetIndexSpanTags.DocumentId));
        Assert.Equal("tenant-a", span.GetTagItem(NetIndexSpanTags.TenantId));
        Assert.NotEqual(ActivityStatusCode.Error, span.Status);
    }

    private sealed class NoDeleteStore : IVectorStore
    {
        public int Dimensions => 3;

        public Task UpsertAsync(IEnumerable<RagChunk> chunks, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async IAsyncEnumerable<SearchResult<RagChunk>> QueryAsync(
            float[] queryVector, int top = 5, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task DeleteAsync(string documentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task A_pipeline_that_does_not_override_DeleteAsync_fails_closed_Async()
    {
        INetIndexPipeline pipeline = new PipelineWithoutDelete();

        var ex = await Assert.ThrowsAsync<NetIndexConfigurationException>(() => pipeline.DeleteAsync("doc-1"));

        Assert.Contains(nameof(INetIndexPipeline.DeleteAsync), ex.Message);
    }

    private sealed class PipelineWithoutDelete : INetIndexPipeline
    {
        public Task IngestAsync(IDocument document, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async IAsyncEnumerable<SearchResult<RagChunk>> QueryAsync(
            string query, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<GenerationChunk> GenerateAsync(
            string query, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
