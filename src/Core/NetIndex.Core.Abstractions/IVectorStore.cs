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
    /// The <see cref="NetIndexAuthorizationException.FailureReason"/> used when a replace would touch
    /// chunks owned by another tenant or another document.
    /// </summary>
    const string CROSS_TENANT_DOCUMENT_COLLISION = "CrossTenantDocumentCollision";

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
    /// Document ids are ONE GLOBAL NAMESPACE across tenants: chunk ids are <c>{documentId}_chunk_{i}</c>
    /// and are globally unique. Multi-tenant hosts must mint opaque, tenant-unique document ids.
    /// </para>
    /// <para>
    /// Fail-closed tenant check, run inside the same transaction or lock as the replace: the replace is
    /// rejected with <see cref="NetIndexAuthorizationException"/> (<see cref="CROSS_TENANT_DOCUMENT_COLLISION"/>)
    /// and nothing is written or deleted when (a) any existing chunk of <paramref name="documentId"/> carries a
    /// different <see cref="RagChunkMetadata.TenantId"/> than the new chunks (including none versus some), or
    /// (b) any new chunk id already exists under a different document id or a different tenant.
    /// An empty <paramref name="chunks"/> set carries no tenant and therefore cannot replace tenant-stamped chunks.
    /// Only this method (and <see cref="INetIndexPipeline.IngestAsync"/>, which calls it) enforces the check;
    /// <see cref="UpsertAsync"/> and <see cref="DeleteAsync"/> do not.
    /// </para>
    /// <para>
    /// The default implementation FAILS CLOSED: it throws <see cref="NetIndexConfigurationException"/> and
    /// writes and deletes nothing. A store used for ingestion must override this method.
    /// </para>
    /// </remarks>
    /// <param name="documentId">The document whose chunks are replaced.</param>
    /// <param name="chunks">The new chunk set. Every chunk must belong to <paramref name="documentId"/>, have a unique id and carry the same tenant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="NetIndexConfigurationException">Thrown by the default implementation: the store does not implement replace.</exception>
    /// <exception cref="NetIndexAuthorizationException">Thrown when the replace would touch another tenant's or document's chunks.</exception>
    /// <exception cref="ArgumentException">Thrown for a malformed set (foreign document, duplicate chunk id, mixed tenants).</exception>
    Task ReplaceDocumentAsync(
        string documentId,
        IEnumerable<RagChunk> chunks,
        CancellationToken cancellationToken = default) =>
        throw new NetIndexConfigurationException(
            $"{GetType().Name} does not implement IVectorStore.ReplaceDocumentAsync. " +
            "A vector store used for ingestion must implement it (atomic, tenant-checked replace).",
            nameof(ReplaceDocumentAsync),
            "An IVectorStore that overrides ReplaceDocumentAsync",
            GetType().FullName);

    /// <summary>
    /// Validates a replacement set for <see cref="ReplaceDocumentAsync"/> and returns the tenant it carries.
    /// Intended for store implementations.
    /// </summary>
    /// <param name="documentId">The document being replaced.</param>
    /// <param name="chunks">The new chunk set.</param>
    /// <returns>The single <see cref="RagChunkMetadata.TenantId"/> carried by the new chunks, or null when none carries one.</returns>
    /// <exception cref="ArgumentException">Thrown when a chunk belongs to another document, a chunk id repeats, or the chunks carry more than one tenant.</exception>
    static string? ValidateReplacement(string documentId, IReadOnlyList<RagChunk> chunks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(chunks);

        string? tenant = null;
        var first = true;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in chunks)
        {
            ArgumentNullException.ThrowIfNull(chunk);
            if (!string.Equals(chunk.DocumentId, documentId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Chunk '{chunk.Id}' belongs to document '{chunk.DocumentId}', not '{documentId}'.",
                    nameof(chunks));
            }

            if (!ids.Add(chunk.Id))
            {
                throw new ArgumentException($"Duplicate chunk id '{chunk.Id}' in the replacement set.", nameof(chunks));
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
                throw new ArgumentException("The replacement set carries more than one tenant.", nameof(chunks));
            }
        }

        return tenant;
    }

    /// <summary>
    /// Creates the exception thrown when a replace would touch chunks owned by another tenant or document.
    /// Intended for store implementations.
    /// </summary>
    /// <param name="documentId">The colliding document identifier.</param>
    /// <param name="attemptedTenantId">The tenant attempting the replace, if known.</param>
    /// <returns>A <see cref="NetIndexAuthorizationException"/> with failure reason <see cref="CROSS_TENANT_DOCUMENT_COLLISION"/>.</returns>
    static NetIndexAuthorizationException CrossTenantCollision(string documentId, string? attemptedTenantId) =>
        new(
            $"Document '{documentId}' collides with chunks owned by a different tenant or document. Replace rejected; nothing was written or deleted.",
            attemptedTenantId,
            null,
            CROSS_TENANT_DOCUMENT_COLLISION);
}
