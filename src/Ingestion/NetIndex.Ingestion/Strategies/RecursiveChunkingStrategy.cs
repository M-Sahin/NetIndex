using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Options;

namespace NetIndex.Ingestion.Strategies;

/// <summary>
/// Attempts fixed-size chunking first; falls back to semantic chunking for segments that exceed the size limit.
/// </summary>
/// <remarks>
/// This strategy provides a best-effort approach: clean fixed-size splits when the text is uniform,
/// and semantic boundary detection when the text has varied topic structure that a naive split would break.
/// </remarks>
public sealed class RecursiveChunkingStrategy : IChunkingStrategy
{
    private const int CharsPerToken = 4;

    private readonly FixedSizeChunkingStrategy _fixedSizeStrategy;
    private readonly SemanticChunkingStrategy _semanticStrategy;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecursiveChunkingStrategy"/> class.
    /// </summary>
    /// <param name="embeddingGenerator">The embedding generator for semantic fallback.</param>
    /// <param name="configuration">The chunking configuration.</param>
    public RecursiveChunkingStrategy(
        IEmbeddingGenerator embeddingGenerator,
        IOptions<ChunkingConfiguration> configuration)
    {
        ArgumentNullException.ThrowIfNull(embeddingGenerator);
        ArgumentNullException.ThrowIfNull(configuration);
        _fixedSizeStrategy = new FixedSizeChunkingStrategy(configuration);
        _semanticStrategy = new SemanticChunkingStrategy(embeddingGenerator, configuration);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RagChunk>> ChunkAsync(string text, ChunkingOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);

        var maxChars = TokensToChars(options.ChunkSize);

        // Stage 1: Attempt fixed-size chunking
        var fixedChunks = await _fixedSizeStrategy.ChunkAsync(text, options, cancellationToken).ConfigureAwait(false);
        var fixedChunksList = fixedChunks.ToList();

        // Check if any chunk exceeds the max character limit
        var oversizedChunks = fixedChunksList
            .Select((chunk, index) => (chunk, index))
            .Where(x => x.chunk.Text.Length > maxChars)
            .ToList();

        if (oversizedChunks.Count == 0)
        {
            return fixedChunksList;
        }

        // Stage 2: For oversized chunks, apply semantic chunking to just those segments
        var result = new List<RagChunk>();
        var currentFixedIndex = 0;

        foreach (var (oversizedChunk, oversizedIndex) in oversizedChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Add all fixed chunks before this oversized one
            while (currentFixedIndex < oversizedIndex)
            {
                result.Add(fixedChunksList[currentFixedIndex]);
                currentFixedIndex++;
            }

            // Apply semantic chunking to the oversized segment
            var semanticChunks = await _semanticStrategy
                .ChunkAsync(oversizedChunk.Text, options, cancellationToken)
                .ConfigureAwait(false);

            result.AddRange(semanticChunks);
            currentFixedIndex = oversizedIndex + 1;
        }

        // Add any remaining fixed chunks after the last oversized one
        while (currentFixedIndex < fixedChunksList.Count)
        {
            result.Add(fixedChunksList[currentFixedIndex]);
            currentFixedIndex++;
        }

        // Re-index chunk IDs to be sequential
        return result.Select((chunk, index) => new RagChunk(
            $"chunk_{index}",
            chunk.Text,
            null,
            "pending",
            null));
    }

    private static int TokensToChars(int tokens) => tokens * CharsPerToken;
}