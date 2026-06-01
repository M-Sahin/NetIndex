using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetIndex.AspNetCore.BackgroundServices;
using NetIndex.AspNetCore.Middleware;
using NetIndex.AspNetCore.Options;
using NetIndex.Core.Abstractions;
using NSubstitute;
using Xunit;

namespace NetIndex.AspNetCore.Tests.BackgroundServices;

/// <summary>Unit tests for <see cref="IngestionHostedService"/>.</summary>
public class IngestionHostedServiceTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private sealed class Harness
    {
        public INetIndexPipeline Pipeline { get; } = Substitute.For<INetIndexPipeline>();
        public CapturingLogger<IngestionHostedService> Logger { get; } = new();
        public ChannelIngestionQueue Queue { get; }
        public IngestionHostedService Service { get; }

        public Harness()
        {
            var accessor = new HttpContextAccessor();
            Queue = new ChannelIngestionQueue(
                Microsoft.Extensions.Options.Options.Create(new BackgroundIngestionOptions()), accessor);

            var services = new ServiceCollection();
            services.AddSingleton(Pipeline);
            var provider = services.BuildServiceProvider();

            Service = new IngestionHostedService(
                Queue,
                provider.GetRequiredService<IServiceScopeFactory>(),
                accessor,
                Logger);
        }
    }

    private static IDocument Document(string id)
    {
        var document = Substitute.For<IDocument>();
        document.Id.Returns(id);
        return document;
    }

    /// <summary>An enqueued item is drained and ingested through the pipeline.</summary>
    [Fact]
    public async Task IngestionHostedService_ProcessesEnqueuedItem_CallsPipelineIngestAsync()
    {
        var harness = new Harness();
        var ingested = new TaskCompletionSource();
        harness.Pipeline.IngestAsync(Arg.Any<IDocument>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                ingested.TrySetResult();
                return Task.CompletedTask;
            });

        await harness.Queue.EnqueueAsync(Document("doc-1"));
        await harness.Service.StartAsync(CancellationToken.None);

        await ingested.Task.WaitAsync(WaitTimeout);
        await harness.Service.StopAsync(CancellationToken.None);

        await harness.Pipeline.Received(1)
            .IngestAsync(Arg.Is<IDocument>(d => d.Id == "doc-1"), Arg.Any<CancellationToken>());
    }

    /// <summary>A failing document is logged and skipped; the next document is still ingested.</summary>
    [Fact]
    public async Task IngestionHostedService_PoisonDocument_IsLoggedAndSkipped_NextItemProcessedAsync()
    {
        var harness = new Harness();
        var goodProcessed = new TaskCompletionSource();
        harness.Pipeline.IngestAsync(Arg.Any<IDocument>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var document = callInfo.Arg<IDocument>();
                if (document.Id == "poison")
                {
                    throw new InvalidOperationException("boom");
                }

                goodProcessed.TrySetResult();
                return Task.CompletedTask;
            });

        await harness.Queue.EnqueueAsync(Document("poison"));
        await harness.Queue.EnqueueAsync(Document("good"));
        await harness.Service.StartAsync(CancellationToken.None);

        await goodProcessed.Task.WaitAsync(WaitTimeout);
        await harness.Service.StopAsync(CancellationToken.None);

        harness.Service.ExecuteTask.Should().NotBeNull();
        harness.Service.ExecuteTask!.IsFaulted.Should().BeFalse();
        harness.Logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Error && e.Message.Contains("poison"));
    }

    /// <summary>Stopping an idle service completes without throwing.</summary>
    [Fact]
    public async Task IngestionHostedService_StopAsync_CompletesWithoutThrowingAsync()
    {
        var harness = new Harness();

        await harness.Service.StartAsync(CancellationToken.None);
        var act = async () => await harness.Service.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        harness.Service.ExecuteTask.Should().NotBeNull();
        harness.Service.ExecuteTask!.IsFaulted.Should().BeFalse();
    }

    /// <summary>
    /// Captured tenant context (TenantId + Claims) is replayed onto IHttpContextAccessor before
    /// IngestAsync; after a poison item the finally guard clears the context so the next item
    /// sees a fresh empty one rather than the poison item's tenant.
    /// </summary>
    [Fact]
    public async Task IngestionHostedService_TenantContextReplayed_BeforeIngestAndClearedAfterPoisonAsync()
    {
        var accessor = new HttpContextAccessor();
        var queue = new ChannelIngestionQueue(
            Microsoft.Extensions.Options.Options.Create(new BackgroundIngestionOptions()), accessor);

        var pipeline = Substitute.For<INetIndexPipeline>();
        var services = new ServiceCollection();
        services.AddSingleton(pipeline);
        var provider = services.BuildServiceProvider();

        var service = new IngestionHostedService(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            accessor,
            new CapturingLogger<IngestionHostedService>());

        // Enqueue a poison item with tenant context captured from a fake request.
        var requestContext = new DefaultHttpContext();
        requestContext.Items[NetIndexTenantMiddleware.TenantContextKey] = "acme";
        requestContext.Items[NetIndexTenantMiddleware.ClaimsContextKey] =
            (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["role"] = "admin" };
        accessor.HttpContext = requestContext;
        await queue.EnqueueAsync(Document("poison"));
        accessor.HttpContext = null; // simulate end of HTTP request

        // Enqueue a tenant-free good item so we observe the post-poison accessor state.
        await queue.EnqueueAsync(Document("good"));

        HttpContext? contextDuringPoison = null;
        bool tenantKeyAbsentAfterPoison = false;
        var goodProcessed = new TaskCompletionSource();

        pipeline.IngestAsync(Arg.Any<IDocument>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var doc = callInfo.Arg<IDocument>();
                if (doc.Id == "poison")
                {
                    contextDuringPoison = accessor.HttpContext;
                    throw new InvalidOperationException("poison item!");
                }

                // After poison's finally cleared the accessor, ProcessItemAsync sets a fresh
                // empty context for the good item (no TenantContextKey).
                tenantKeyAbsentAfterPoison =
                    accessor.HttpContext?.Items.ContainsKey(NetIndexTenantMiddleware.TenantContextKey) != true;
                goodProcessed.TrySetResult();
                return Task.CompletedTask;
            });

        await service.StartAsync(CancellationToken.None);
        await goodProcessed.Task.WaitAsync(WaitTimeout);
        await service.StopAsync(CancellationToken.None);

        // Tenant context was replayed before the poison item's IngestAsync.
        contextDuringPoison.Should().NotBeNull();
        contextDuringPoison!.Items[NetIndexTenantMiddleware.TenantContextKey].Should().Be("acme");
        var capturedClaims = contextDuringPoison.Items[NetIndexTenantMiddleware.ClaimsContextKey]
            as IReadOnlyDictionary<string, string>;
        capturedClaims.Should().NotBeNull();
        capturedClaims!["role"].Should().Be("admin");

        // After poison's finally ran and the good item started, the poison TenantContextKey is gone.
        tenantKeyAbsentAfterPoison.Should().BeTrue("finally must clear the poison item's context");
    }
}

/// <summary>An <see cref="ILogger{TCategoryName}"/> that records entries for assertion.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (Entries)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
