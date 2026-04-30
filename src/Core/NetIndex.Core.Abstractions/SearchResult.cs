namespace NetIndex.Core.Abstractions;

/// <summary>
/// Represents a single search result with a relevance score.
/// </summary>
/// <remarks>
/// Canonical noun #5 in NOUNS.md.
/// 
/// <para>The <see cref="Score"/> property typically ranges from 0.0 to 1.0, where higher values indicate
/// greater relevance. The exact range depends on the vector store's distance metric
/// (cosine similarity, dot product, euclidean distance).</para>
/// 
/// <para>Results are ordered by descending score (most relevant first).</para>
/// </remarks>
/// <param name="Item">The item that matched the search query.</param>
/// <param name="Score">Relevance score. Higher = more relevant. Typical range: 0.0–1.0.</param>
/// <param name="DocumentId">The identifier of the document this result came from.</param>
public record SearchResult<T>(
    T Item,
    float Score,
    string DocumentId)
    where T : notnull;
