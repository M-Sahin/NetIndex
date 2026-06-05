namespace NetIndex.Core.Abstractions.Telemetry;

/// <summary>
/// Tag key constants for NetIndex pipeline spans.
/// </summary>
/// <remarks>
/// All constants are <c>public const string</c> — the compiler inlines them at call sites
/// so there is zero runtime allocation. Use these with
/// <see cref="NetIndexActivitySource"/> when calling <c>Activity.SetTag()</c> to ensure
/// consistent tag key naming across packages. Story 6.3 (structured logging) reuses these
/// same keys so spans and log events remain correlatable.
/// </remarks>
public static class NetIndexSpanTags
{
    /// <summary>Tenant identifier for the request: <c>netindex.tenant_id</c>.</summary>
    public const string TenantId = "netindex.tenant_id";

    /// <summary>Source document identifier: <c>netindex.document_id</c>.</summary>
    public const string DocumentId = "netindex.document_id";

    /// <summary>Number of chunks produced or processed: <c>netindex.chunk_count</c>.</summary>
    public const string ChunkCount = "netindex.chunk_count";

    /// <summary>Number of embedding vectors generated: <c>netindex.embedding_count</c>.</summary>
    public const string EmbeddingCount = "netindex.embedding_count";

    /// <summary>Dimensionality of the embedding vectors: <c>netindex.embedding_dimensions</c>.</summary>
    public const string EmbeddingDimensions = "netindex.embedding_dimensions";

    /// <summary>The <c>top</c> (fetch count) passed to the vector store: <c>netindex.retrieve.top</c>.</summary>
    public const string RetrieveTop = "netindex.retrieve.top";

    /// <summary>Raw result count returned by the vector store before tenant filtering: <c>netindex.retrieve.result_count</c>.</summary>
    public const string RetrieveResultCount = "netindex.retrieve.result_count";

    /// <summary>Result count after tenant filtering: <c>netindex.retrieve.filtered_count</c>.</summary>
    public const string RetrieveFilteredCount = "netindex.retrieve.filtered_count";

    /// <summary>Number of retrieved chunks forwarded to the LLM context: <c>netindex.context_chunk_count</c>.</summary>
    public const string ContextChunkCount = "netindex.context_chunk_count";
}
