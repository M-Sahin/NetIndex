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

    /// <summary>
    /// Replaces the complete chunk set of a document with <paramref name="chunks"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The built-in stores (in-memory, SQLite, pgvector) override this method and apply the replace
    /// atomically: a concurrent <see cref="QueryAsync"/> sees either the complete old set or the complete
    /// new set, never a mix and never neither. A failure leaves the old set intact.
    /// </para>
    /// <para>
    /// Fail-closed tenant check: when any existing chunk of <paramref name="documentId"/> carries a
    /// different <see cref="RagChunkMetadata.TenantId"/> than the new chunks (including carrying none while
    /// the new chunks do, or the reverse), the replace is rejected with <see cref="NetIndexAuthorizationException"/>
    /// and nothing is written or deleted. The check runs inside the same transaction or lock as the replace.
    /// An empty <paramref name="chunks"/> set carries no tenant and therefore cannot replace tenant-stamped chunks.
    /// </para>
    /// <para>
    /// This default implementation is delete-then-upsert. It is NOT atomic and performs NO tenant check;
    /// custom stores should override it.
    /// </para>
    /// </remarks>
    /// <param name="documentId">The document whose chunks are replaced.</param>
    /// <param name="chunks">The new chunk set. Every chunk must belong to <paramref name="documentId"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="NetIndexAuthorizationException">Thrown when an existing chunk belongs to another tenant (built-in stores).</exception>
    async Task ReplaceDocumentAsync(
        string documentId,
        IEnumerable<RagChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var chunkList = chunks?.ToList() ?? throw new ArgumentNullException(nameof(chunks));
        _ = ValidateReplacement(documentId, chunkList);
        await DeleteAsync(documentId, cancellationToken).ConfigureAwait(false);
        await UpsertAsync(chunkList, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates a replacement set for <see cref="ReplaceDocumentAsync"/> and returns the tenant it carries.
    /// Intended for store implementations.
    /// </summary>
    /// <param name="documentId">The document being replaced.</param>
    /// <param name="chunks">The new chunk set.</param>
    /// <returns>The single <see cref="RagChunkMetadata.TenantId"/> carried by the new chunks, or null when none carries one.</returns>
    /// <exception cref="ArgumentException">Thrown when a chunk belongs to another document.</exception>
    /// <exception cref="NetIndexAuthorizationException">Thrown when the new chunks carry more than one tenant.</exception>
    static string? ValidateReplacement(string documentId, IReadOnlyList<RagChunk> chunks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(chunks);

        string? tenant = null;
        var first = true;
        foreach (var chunk in chunks)
        {
            ArgumentNullException.ThrowIfNull(chunk);
            if (!string.Equals(chunk.DocumentId, documentId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Chunk '{chunk.Id}' belongs to document '{chunk.DocumentId}', not '{documentId}'.",
                    nameof(chunks));
            }

            string? chunkTenant = null;
            chunk.Metadata?.TryGetValue(RagChunkMetadata.TenantId, out chunkTenant);
            if (first)
            {
                tenant = chunkTenant;
                first = false;
            }
            else if (!string.Equals(tenant, chunkTenant, StringComparison.Ordinal))
            {
                throw CrossTenantCollision(documentId, tenant);
            }
        }

        return tenant;
    }

    /// <summary>
    /// Creates the exception thrown when a replace would touch chunks owned by another tenant.
    /// Intended for store implementations.
    /// </summary>
    /// <param name="documentId">The colliding document identifier.</param>
    /// <param name="attemptedTenantId">The tenant attempting the replace, if known.</param>
    /// <returns>A <see cref="NetIndexAuthorizationException"/> with failure reason <c>CrossTenantDocumentCollision</c>.</returns>
    static NetIndexAuthorizationException CrossTenantCollision(string documentId, string? attemptedTenantId) =>
        new(
            $"Document '{documentId}' already exists with a different tenant. Replace rejected; nothing was written or deleted.",
            attemptedTenantId,
            null,
            "CrossTenantDocumentCollision");
}
