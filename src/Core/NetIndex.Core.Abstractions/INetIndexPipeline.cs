using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NetIndex.Core.Abstractions;

/// <summary>
/// High-level RAG pipeline that coordinates ingest, query, and generation flows.
/// </summary>
/// <remarks>
/// Canonical noun #18 (Pipeline) in NOUNS.md.
///
/// The concrete implementation (<c>NetIndexPipeline</c>) lives in the <c>NetIndex.Core</c> package.
/// All methods enforce authorization via <see cref="ITenantResolver"/> before any pipeline work.
/// </remarks>
public interface INetIndexPipeline
{
    /// <summary>
    /// Ingests a document through the full pipeline: chunk → embed → store.
    /// </summary>
    /// <param name="document">The document to ingest. Content must be non-null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="NetIndexAuthorizationException">Thrown when authorization fails.</exception>
    /// <exception cref="ArgumentException">Thrown when the document id is blank, the content is empty or whitespace, or the content yields no non-blank chunks. Nothing is written or deleted.</exception>
    /// <exception cref="NetIndexAuthorizationException">Also thrown (<c>CrossTenantDocumentCollision</c>) when the document id is already held by another tenant. Nothing is written or deleted.</exception>
    /// <exception cref="NetIndexProviderException">Thrown when a pipeline stage (chunk/embed/store) fails.</exception>
    Task IngestAsync(IDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every chunk of a document that belongs to the current tenant.
    /// </summary>
    /// <remarks>
    /// The tenant comes only from <see cref="ITenantResolver"/>, never from a parameter. Chunks of the same
    /// document id that belong to another tenant, or to no tenant, are left untouched. A missing or foreign
    /// id is a silent no-op, so deleting is idempotent and the delete itself reveals nothing about other
    /// tenants' ids. Document ids remain one global namespace (an ingest of an id another tenant holds is
    /// rejected with <c>CrossTenantDocumentCollision</c>), so hosts must still mint opaque, tenant-unique
    /// document ids; see the <see cref="IVectorStore"/> remarks.
    /// </remarks>
    /// <param name="documentId">The document whose chunks are removed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="NetIndexAuthorizationException">Thrown when authorization fails, including a null or blank
    /// tenant from the resolver (<c>BlankTenantId</c>). No store call is made.</exception>
    /// <exception cref="ArgumentException">Thrown when the document id is null, empty or whitespace. Raised after
    /// authorization and before any store call.</exception>
    /// <exception cref="NetIndexConfigurationException">Thrown when the vector store does not implement tenant-scoped removal,
    /// or when this pipeline implementation does not implement <see cref="DeleteAsync"/> (the fail-closed default).</exception>
    /// <exception cref="NetIndexStorageException">Thrown, unwrapped, when a built-in store fails (e.g. a database error).</exception>
    /// <exception cref="NetIndexProviderException">Thrown when the store call fails with a non-NetIndex exception.</exception>
    Task DeleteAsync(string documentId, CancellationToken cancellationToken = default) =>
        throw new NetIndexConfigurationException(
            $"{GetType().Name} does not implement INetIndexPipeline.DeleteAsync. " +
            "A pipeline used for removal must implement it (authorize first, tenant-scoped delete).",
            nameof(DeleteAsync),
            "An INetIndexPipeline that overrides DeleteAsync",
            GetType().FullName);

    /// <summary>
    /// Queries the vector store for chunks relevant to the given query text.
    /// </summary>
    /// <param name="query">The search query text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Streaming results ordered by descending relevance score.</returns>
    /// <exception cref="NetIndexAuthorizationException">Thrown when authorization fails.</exception>
    /// <exception cref="NetIndexProviderException">Thrown when embedding or vector store query fails.</exception>
    IAsyncEnumerable<SearchResult<RagChunk>> QueryAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a streaming LLM answer grounded on retrieved document chunks.
    /// </summary>
    /// <param name="query">The user query or prompt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Streaming generation tokens. The final chunk has <c>IsComplete = true</c>.</returns>
    /// <exception cref="NetIndexAuthorizationException">Thrown when authorization fails.</exception>
    /// <exception cref="NetIndexProviderException">Thrown when query or generation fails.</exception>
    IAsyncEnumerable<GenerationChunk> GenerateAsync(
        string query,
        CancellationToken cancellationToken = default);
}
