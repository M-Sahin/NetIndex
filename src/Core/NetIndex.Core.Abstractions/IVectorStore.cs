using System.Threading;
using System.Threading.Tasks;

namespace NetIndex.Core.Abstractions;

/// <summary>
/// Persists and retrieves document vectors for similarity search.
/// </summary>
/// <remarks>
/// Canonical noun #10 (Store) in NOUNS.md.
/// 
/// Implementations include in-memory, SQLite (sqlite-vec), and pgvector backends.
/// The <see cref="Dimensions"/> property is validated against the embedding generator
/// at pipeline startup to prevent silent dimension mismatches.
/// </remarks>
public interface IVectorStore
{
    /// <summary>
    /// Gets the number of dimensions this store accepts for vector operations.
    /// </summary>
    /// <remarks>
    /// Must match <see cref="IEmbeddingGenerator.Dimensions"/> at startup.
    /// </remarks>
    int Dimensions { get; }

    /// <summary>
    /// Inserts or updates chunks in the vector store.
    /// </summary>
    /// <param name="chunks">Chunks to upsert — each must have a non-null <see cref="RagChunk.Embedding"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpsertAsync(IEnumerable<RagChunk> chunks, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a vector similarity search, returning results as a streaming enumerable.
    /// </summary>
    /// <param name="queryVector">The embedding vector to search against.</param>
    /// <param name="top">Maximum number of results to return (default: 5).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Results ordered by descending relevance score.</returns>
    IAsyncEnumerable<SearchResult<RagChunk>> QueryAsync(
        float[] queryVector,
        int top = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all chunks associated with a document.
    /// </summary>
    /// <param name="documentId">The document identifier to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(string documentId, CancellationToken cancellationToken = default);
}
