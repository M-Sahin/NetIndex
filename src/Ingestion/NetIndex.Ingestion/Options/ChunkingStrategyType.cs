namespace NetIndex.Ingestion.Options;

/// <summary>
/// Identifies which chunking strategy to register in the DI container.
/// </summary>
public enum ChunkingStrategyType
{
    /// <summary>
    /// Splits text into chunks of a fixed token count with configurable overlap.
    /// </summary>
    FixedSize,

    /// <summary>
    /// Splits text at semantic boundaries (topic changes) using embedding similarity.
    /// </summary>
    Semantic,

    /// <summary>
    /// Attempts fixed-size first; falls back to semantic for segments that exceed the size limit.
    /// </summary>
    Recursive,
}