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
    /// <exception cref="NetIndexProviderException">Thrown when a pipeline stage (chunk/embed/store) fails.</exception>
    Task IngestAsync(IDocument document, CancellationToken cancellationToken = default);

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
