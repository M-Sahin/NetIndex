using System.Threading;
using System.Threading.Tasks;

namespace NetIndex.Core.Abstractions;

/// <summary>
/// Re-ranks search results based on query relevance.
/// </summary>
/// <remarks>
/// Canonical noun #17 (Reranker) in NOUNS.md.
/// 
/// Reranking is an optional post-retrieval step that applies a more expensive
/// relevance model (e.g., cross-encoder) to the top-K results from vector search.
/// This interface is defined for V1 but may be a no-op placeholder until a
/// concrete reranker implementation is added.
/// </remarks>
public interface IDocumentReranker
{
    /// <summary>
    /// Re-ranks the given search results based on their relevance to the query.
    /// </summary>
    /// <param name="results">The original search results from vector similarity search.</param>
    /// <param name="query">The original user query to score against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Re-ranked results, ordered by descending relevance score.</returns>
    Task<IEnumerable<SearchResult<RagChunk>>> RerankAsync(
        IEnumerable<SearchResult<RagChunk>> results,
        string query,
        CancellationToken cancellationToken = default);
}
