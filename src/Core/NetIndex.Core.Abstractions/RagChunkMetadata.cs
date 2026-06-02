namespace NetIndex.Core.Abstractions;

/// <summary>
/// Reserved metadata keys used by the NetIndex framework on <see cref="RagChunk.Metadata"/>.
/// </summary>
/// <remarks>
/// Keys in this class are framework-owned. Callers must not pre-populate them on chunks passed to
/// <see cref="INetIndexPipeline.IngestAsync"/>; doing so raises a
/// <see cref="NetIndexAuthorizationException"/> with
/// <c>FailureReason = "ReservedMetadataKeyConflict"</c>.
/// </remarks>
public static class RagChunkMetadata
{
    /// <summary>
    /// The metadata key under which the pipeline stores the resolved tenant identifier at ingest time.
    /// </summary>
    /// <remarks>
    /// Value: <c>"netindex:tenant_id"</c>. The pipeline stamps every persisted
    /// <see cref="RagChunk"/> with this key so that tenant-scoped retrieval can filter results
    /// without modifying the <see cref="IVectorStore"/> contract.
    /// </remarks>
    public const string TenantId = "netindex:tenant_id";
}
