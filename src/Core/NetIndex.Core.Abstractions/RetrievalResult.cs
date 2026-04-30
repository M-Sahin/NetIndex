namespace NetIndex.Core.Abstractions;

/// <summary>
/// Represents the complete result of a retrieval operation against a vector store.
/// </summary>
/// <remarks>
/// Canonical noun #6 in NOUNS.md. This is a forward declaration; full definition moves to story 1.3.
/// </remarks>
/// <param name="Query">The original query text.</param>
/// <param name="Results">Search results ordered by relevance (descending score).</param>
/// <param name="Duration">Time elapsed to produce this result.</param>
public partial record RetrievalResult(
    string Query,
    IReadOnlyList<SearchResult<RagChunk>> Results,
    TimeSpan Duration);
