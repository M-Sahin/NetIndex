using NetIndex.Core.Abstractions;

namespace NetIndex.Ingestion.Options;

/// <summary>
/// Fluent configuration class for selecting and parameterizing a chunking strategy.
/// Registered as <c>IOptions</c> of <c>ChunkingConfiguration</c> in DI.
/// </summary>
/// <remarks>
/// Use the fluent methods <see cref="FixedSize(int, int)"/>, <see cref="Semantic(int?, int?)"/>,
/// or <see cref="Recursive(int?, int?)"/> to configure the desired strategy.
/// </remarks>
public sealed class ChunkingConfiguration
{
    /// <summary>
    /// Gets the strategy selected via one of the fluent methods.
    /// </summary>
    internal ChunkingStrategyType SelectedStrategy { get; set; }

    /// <summary>
    /// Gets or sets the chunk size for <see cref="ChunkingStrategyType.FixedSize"/>.
    /// </summary>
    internal int FixedSizeChunkSize { get; set; } = 512;

    /// <summary>
    /// Gets or sets the overlap for <see cref="ChunkingStrategyType.FixedSize"/>.
    /// </summary>
    internal int FixedSizeOverlap { get; set; } = 64;

    /// <summary>
    /// Gets or sets the maximum chunk size for <see cref="ChunkingStrategyType.Semantic"/>.
    /// </summary>
    internal int? SemanticMaxChunkSize { get; set; }

    /// <summary>
    /// Gets or sets the overlap for <see cref="ChunkingStrategyType.Semantic"/>.
    /// </summary>
    internal int? SemanticOverlap { get; set; }

    /// <summary>
    /// Gets or sets the maximum chunk size for <see cref="ChunkingStrategyType.Recursive"/>.
    /// </summary>
    internal int? RecursiveMaxChunkSize { get; set; }

    /// <summary>
    /// Gets or sets the overlap for <see cref="ChunkingStrategyType.Recursive"/>.
    /// </summary>
    internal int? RecursiveOverlap { get; set; }

    /// <summary>
    /// Gets or sets the separator to use when splitting text into segments.
    /// </summary>
    internal string Separator { get; set; } = "\n";

    /// <summary>
    /// Configures the fixed-size chunking strategy.
    /// </summary>
    /// <param name="chunkSize">Target number of tokens per chunk. Must be greater than zero.</param>
    /// <param name="overlap">Number of tokens to overlap between consecutive chunks. Must be &gt;= 0 and &lt; <paramref name="chunkSize"/>.</param>
    /// <returns>This instance for fluent chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="chunkSize"/> &lt;= 0 or <paramref name="overlap"/> is out of range.</exception>
    public ChunkingConfiguration FixedSize(int chunkSize, int overlap)
    {
        ValidateChunkParameters(chunkSize, overlap);
        SelectedStrategy = ChunkingStrategyType.FixedSize;
        FixedSizeChunkSize = chunkSize;
        FixedSizeOverlap = overlap;
        return this;
    }

    /// <summary>
    /// Configures the semantic chunking strategy.
    /// </summary>
    /// <param name="maxChunkSize">Optional maximum chunk size in tokens. Uses <see cref="FixedSizeChunkSize"/> if not specified.</param>
    /// <param name="overlap">Optional token overlap between chunks.</param>
    /// <returns>This instance for fluent chaining.</returns>
    public ChunkingConfiguration Semantic(int? maxChunkSize = null, int? overlap = null)
    {
        SelectedStrategy = ChunkingStrategyType.Semantic;
        SemanticMaxChunkSize = maxChunkSize;
        SemanticOverlap = overlap;
        return this;
    }

    /// <summary>
    /// Configures the recursive chunking strategy.
    /// </summary>
    /// <param name="maxChunkSize">Optional maximum chunk size in tokens. Uses <see cref="FixedSizeChunkSize"/> if not specified.</param>
    /// <param name="overlap">Optional token overlap between chunks.</param>
    /// <returns>This instance for fluent chaining.</returns>
    public ChunkingConfiguration Recursive(int? maxChunkSize = null, int? overlap = null)
    {
        SelectedStrategy = ChunkingStrategyType.Recursive;
        RecursiveMaxChunkSize = maxChunkSize;
        RecursiveOverlap = overlap;
        return this;
    }

    /// <summary>
    /// Converts this configuration to a <see cref="ChunkingOptions"/> record for use by strategies.
    /// </summary>
    internal ChunkingOptions ToChunkingOptions()
    {
        var (size, overlap) = SelectedStrategy switch
        {
            ChunkingStrategyType.FixedSize => (FixedSizeChunkSize, FixedSizeOverlap),
            ChunkingStrategyType.Semantic => (SemanticMaxChunkSize ?? FixedSizeChunkSize, SemanticOverlap ?? FixedSizeOverlap),
            ChunkingStrategyType.Recursive => (RecursiveMaxChunkSize ?? FixedSizeChunkSize, RecursiveOverlap ?? FixedSizeOverlap),
            _ => (FixedSizeChunkSize, FixedSizeOverlap),
        };

        return new ChunkingOptions(size, overlap, Separator);
    }

    private static void ValidateChunkParameters(int chunkSize, int overlap)
    {
        if (chunkSize <= 0)
        {
            throw new ArgumentException("ChunkSize must be greater than zero.", nameof(chunkSize));
        }

        if (overlap < 0)
        {
            throw new ArgumentException("Overlap must be non-negative.", nameof(overlap));
        }

        if (overlap >= chunkSize)
        {
            throw new ArgumentException("Overlap must be less than ChunkSize.", nameof(overlap));
        }
    }
}