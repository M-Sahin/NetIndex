namespace NetIndex.Core.Abstractions;

/// <summary>
/// Represents a single search result with a relevance score.
/// </summary>
/// <remarks>
/// Canonical noun #5 in NOUNS.md. This is a forward declaration; full definition moves to story 1.3.
/// </remarks>
/// <param name="Item">The item that matched the search query.</param>
/// <param name="Score">Relevance score (higher = more relevant, typically 0.0–1.0).</param>
/// <param name="DocumentId">The identifier of the document this result came from.</param>
public partial record SearchResult<T>(
    T Item,
    float Score,
    string DocumentId)
    where T : notnull;
