using System.Text;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Options;

namespace NetIndex.Ingestion.Strategies;

/// <summary>
/// Splits text into fixed-size chunks with configurable overlap using character-based token approximation.
/// </summary>
/// <remarks>
/// Token approximation: 1 token ≈ 4 characters (rough heuristic for English text).
/// The ChunkSize and ChunkOverlap values from <see cref="ChunkingOptions"/>
/// are in tokens and are converted to character counts internally.
/// </remarks>
public sealed class FixedSizeChunkingStrategy : IChunkingStrategy
{
    private const int CharsPerToken = 4;

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedSizeChunkingStrategy"/> class.
    /// </summary>
    /// <param name="configuration">The chunking configuration.</param>
    public FixedSizeChunkingStrategy(IOptions<ChunkingConfiguration> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        // Configuration is used by the DI factory to select this strategy;
        // chunk sizes are read from the ChunkingOptions parameter at runtime.
        _ = configuration.Value;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The chunking algorithm is synchronous because token approximation is computationally cheap
    /// and requires no I/O. The method returns a completed <see cref="Task{TResult}"/> via
    /// <see cref="Task.FromResult{TResult}(T)"/> to satisfy the <see cref="IChunkingStrategy"/> interface.
    /// </remarks>
    public Task<IEnumerable<RagChunk>> ChunkAsync(string text, ChunkingOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);

        if (options.ChunkSize <= 0)
        {
            throw new ArgumentException("ChunkSize must be greater than zero.", nameof(options));
        }

        if (options.ChunkOverlap < 0 || options.ChunkOverlap >= options.ChunkSize)
        {
            throw new ArgumentException("ChunkOverlap must be >= 0 and < ChunkSize.", nameof(options));
        }

        var maxChars = TokensToChars(options.ChunkSize);
        var overlapChars = TokensToChars(options.ChunkOverlap);
        var separator = options.Separator;

        if (text.Length == 0)
        {
            return Task.FromResult<IEnumerable<RagChunk>>(Array.Empty<RagChunk>());
        }

        var segments = text.Split(separator);
        var chunks = new List<RagChunk>();
        var currentChunk = new StringBuilder();
        var chunkIndex = 0;

        foreach (var segment in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (currentChunk.Length + segment.Length > maxChars && currentChunk.Length > 0)
            {
                chunks.Add(CreateChunk(chunkIndex++, currentChunk, overlapChars));
            }

            currentChunk.Append(segment);
            currentChunk.Append(separator);
        }

        // Last chunk
        if (currentChunk.Length > 0)
        {
            chunks.Add(new RagChunk(
                $"chunk_{chunkIndex}",
                currentChunk.ToString().Trim(),
                null,
                "pending",
                null));
        }

        return Task.FromResult<IEnumerable<RagChunk>>(chunks);
    }

    private RagChunk CreateChunk(int index, StringBuilder currentChunk, int overlapChars)
    {
        var rawText = currentChunk.ToString();
        currentChunk.Clear();

        // Apply overlap: keep last `overlapChars` characters from raw text for the next chunk
        if (overlapChars > 0 && rawText.Length > 0)
        {
            var overlapStart = Math.Max(0, rawText.Length - overlapChars);
            currentChunk.Append(rawText[overlapStart..]);
        }

        return new RagChunk($"chunk_{index}", rawText.Trim(), null, "pending", null);
    }

    /// <summary>
    /// Converts a token count to an approximate character count (1 token ≈ 4 characters).
    /// </summary>
    private static int TokensToChars(int tokens) => tokens * CharsPerToken;
}
