using NetIndex.Core.Abstractions;

namespace NetIndex.AspNetCore.BackgroundServices;

/// <summary>
/// A producer/consumer queue that decouples document intake from ingestion: request handlers
/// enqueue documents and return immediately, while a single background consumer drains and
/// ingests them.
/// </summary>
/// <remarks>
/// Register via <c>INetIndexBuilder.UseBackgroundIngestion(...)</c>. Producers (e.g. an
/// <c>/api/ingest</c> endpoint) call <see cref="EnqueueAsync"/>; the framework's
/// <c>IngestionHostedService</c> is the sole consumer of <see cref="ReadAllAsync"/>.
/// </remarks>
public interface IIngestionQueue
{
    /// <summary>
    /// Enqueues a document for background ingestion, capturing the current request's tenant
    /// context so the background worker can authorize the ingest against the right tenant.
    /// </summary>
    /// <param name="document">The document to ingest.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the work item has been accepted into the queue.</returns>
    /// <remarks>
    /// When the queue is bounded and full, this awaits according to the configured
    /// <c>BackgroundIngestionOptions.FullMode</c> (the default applies backpressure).
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document"/> is null.</exception>
    ValueTask EnqueueAsync(IDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads all queued work items until the queue completes or the token is cancelled. Intended
    /// for a single consumer (the background ingestion service).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token; cancelling ends the enumeration.</param>
    /// <returns>An asynchronous stream of work items.</returns>
    IAsyncEnumerable<IngestionWorkItem> ReadAllAsync(CancellationToken cancellationToken = default);
}
