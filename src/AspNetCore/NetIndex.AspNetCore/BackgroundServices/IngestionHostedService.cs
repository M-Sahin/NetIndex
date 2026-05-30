using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetIndex.AspNetCore.Middleware;
using NetIndex.Core.Abstractions;

namespace NetIndex.AspNetCore.BackgroundServices;

/// <summary>
/// Drains the <see cref="IIngestionQueue"/> and ingests each document through a request-scoped
/// <see cref="INetIndexPipeline"/>. Each work item is processed in its own DI scope with the
/// captured tenant context replayed onto <see cref="IHttpContextAccessor"/>, and a failure on one
/// document is logged and skipped so a poison document never tears down the service.
/// </summary>
internal sealed class IngestionHostedService : BackgroundService
{
    private readonly IIngestionQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<IngestionHostedService> _logger;

    public IngestionHostedService(
        IIngestionQueue queue,
        IServiceScopeFactory scopeFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<IngestionHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(logger);

        _queue = queue;
        _scopeFactory = scopeFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in _queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await ProcessItemAsync(item, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown — exit the drain loop gracefully, not as an error.
        }
    }

    private async Task ProcessItemAsync(IngestionWorkItem item, CancellationToken stoppingToken)
    {
        // Scope creation and pipeline resolution are inside the guarded region so a DI fault
        // on any single item is logged-and-skipped rather than crashing the drain loop.
        // The finally clears the accessor whether or not the item reached IngestAsync.
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var pipeline = scope.ServiceProvider.GetRequiredService<INetIndexPipeline>();

            // Reconstitute the captured tenant context so HttpContextTenantResolver (which reads
            // HttpContext.Items via the AsyncLocal-backed accessor) authorizes against the right
            // tenant. The accessor is cleared in the finally so context never leaks across items.
            _httpContextAccessor.HttpContext = BuildTenantContext(item);
            await pipeline.IngestAsync(item.Document, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Background ingestion failed for document {DocumentId}; skipping.",
                item.Document.Id);
        }
        finally
        {
            _httpContextAccessor.HttpContext = null;
        }
    }

    private static HttpContext BuildTenantContext(IngestionWorkItem item)
    {
        var context = new DefaultHttpContext();
        if (item.TenantId is not null)
        {
            context.Items[NetIndexTenantMiddleware.TenantContextKey] = item.TenantId;
        }

        if (item.Claims is not null)
        {
            context.Items[NetIndexTenantMiddleware.ClaimsContextKey] = item.Claims;
        }

        return context;
    }
}
