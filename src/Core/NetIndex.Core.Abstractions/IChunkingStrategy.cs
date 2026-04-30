using System.Threading;
using System.Threading.Tasks;

namespace NetIndex.Core.Abstractions;

/// <summary>
/// Splits text into chunks using a specific strategy.
/// </summary>
/// <remarks>
/// Canonical noun #16 (Strategy) in NOUNS.md.
/// 
/// V1 includes three strategies:
/// <list type="bullet">
///   <item><term>Fixed-size</term><description>Splits text by a fixed token/character count with configurable overlap.</description></item>
///   <item><term>Semantic</term><description>Splits text at semantic boundaries (sentences, paragraphs) while respecting size limits.</description></item>
///   <item><term>Recursive</term><description>Splits text using a cascade of separators (newline → sentence → word) until chunks fit.</description></item>
/// </list>
/// </remarks>
public interface IChunkingStrategy
{
    /// <summary>
    /// Splits the given text into chunks according to the strategy's algorithm.
    /// </summary>
    /// <param name="text">The full text to chunk.</param>
    /// <param name="options">Configuration for chunk size, overlap, and separator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An enumerable of <see cref="RagChunk"/> instances. Embeddings are NOT set — the pipeline handles embedding separately.</returns>
    Task<IEnumerable<RagChunk>> ChunkAsync(
        string text,
        ChunkingOptions options,
        CancellationToken cancellationToken = default);
}
