using NetIndex.Core.Abstractions;

namespace NetIndex.AspNetCore.BackgroundServices;

/// <summary>
/// An immutable unit of background ingestion work: a document paired with the tenant
/// context snapshotted from the request at enqueue time.
/// </summary>
/// <remarks>
/// The tenant snapshot is captured by <see cref="ChannelIngestionQueue.EnqueueAsync"/> while the
/// originating <c>HttpContext</c> still exists, then replayed by
/// <c>IngestionHostedService</c> so the pipeline's <see cref="ITenantResolver"/> authorizes the
/// background ingest against the correct tenant. Instances are produced by the queue, never by
/// consumers directly.
/// </remarks>
public sealed class IngestionWorkItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionWorkItem"/> class.
    /// </summary>
    /// <param name="document">The document to ingest.</param>
    /// <param name="tenantId">The tenant id captured at enqueue time, or <c>null</c> if none was present.</param>
    /// <param name="claims">The forwarded claims captured at enqueue time, or <c>null</c> if none were present.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document"/> is null.</exception>
    public IngestionWorkItem(
        IDocument document,
        string? tenantId,
        IReadOnlyDictionary<string, string>? claims)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        TenantId = tenantId;
        Claims = claims;
    }

    /// <summary>
    /// Gets the document to ingest.
    /// </summary>
    public IDocument Document { get; }

    /// <summary>
    /// Gets the tenant id captured from the request at enqueue time, or <c>null</c> when no tenant
    /// context was present.
    /// </summary>
    public string? TenantId { get; }

    /// <summary>
    /// Gets the forwarded claims captured from the request at enqueue time, or <c>null</c> when no
    /// claims context was present.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Claims { get; }
}
