namespace NetIndex.Core.Abstractions;

/// <summary>
/// Represents the complete result of a retrieval operation against a vector store.
/// </summary>
/// <remarks>
/// Canonical noun #6 in NOUNS.md.
/// 
/// <para><see cref="Results"/> is ordered by descending relevance score (most relevant first).</para>
/// <para><see cref="Elapsed"/> is always non-negative and represents the wall-clock time to produce this result.</para>
/// </remarks>
/// <param name="Query">The original query text.</param>
/// <param name="Results">Search results ordered by relevance (descending score).</param>
/// <param name="Elapsed">Time elapsed to produce this result (non-negative).</param>
public record RetrievalResult(
    string Query,
    IReadOnlyList<SearchResult<RagChunk>> Results,
    TimeSpan Elapsed);
