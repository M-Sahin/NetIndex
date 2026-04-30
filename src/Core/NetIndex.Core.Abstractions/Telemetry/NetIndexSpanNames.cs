namespace NetIndex.Core.Abstractions.Telemetry;

/// <summary>
/// Span name constants for the five NetIndex pipeline stages.
/// </summary>
/// <remarks>
/// All constants are <c>public const string</c> — the compiler inlines them at call
/// sites so there is zero runtime allocation. Use these with
/// <see cref="NetIndexActivitySource"/> to emit consistent spans across packages.
/// </remarks>
public static class NetIndexSpanNames
{
    /// <summary>Span name for the document ingestion stage: <c>netindex.ingest</c>.</summary>
    /// <remarks>Emitted by <c>NetIndex.Ingestion.*</c> packages when loading documents.</remarks>
    public const string Ingest = "netindex.ingest";

    /// <summary>Span name for the chunking stage: <c>netindex.chunk</c>.</summary>
    /// <remarks>Emitted by chunking strategies in <c>NetIndex.Core</c>.</remarks>
    public const string Chunk = "netindex.chunk";

    /// <summary>Span name for the embedding generation stage: <c>netindex.embed</c>.</summary>
    /// <remarks>Emitted by <c>NetIndex.Providers.*</c> packages when generating vectors.</remarks>
    public const string Embed = "netindex.embed";

    /// <summary>Span name for the vector retrieval stage: <c>netindex.retrieve</c>.</summary>
    /// <remarks>Emitted by <c>NetIndex.Storage.*</c> packages when querying the vector store.</remarks>
    public const string Retrieve = "netindex.retrieve";

    /// <summary>Span name for the LLM generation stage: <c>netindex.generate</c>.</summary>
    /// <remarks>Emitted by <c>NetIndex.Providers.*</c> packages during LLM completion.</remarks>
    public const string Generate = "netindex.generate";
}
