namespace NetIndex.Core.Abstractions;

/// <summary>
/// Represents a chunked text segment from a source document, ready for embedding and storage.
/// </summary>
/// <remarks>
/// Canonical noun #3 in NOUNS.md. This is a forward declaration; full definition moves to story 1.3.
/// </remarks>
/// <param name="Id">Unique identifier for this chunk.</param>
/// <param name="Text">The text content of this chunk.</param>
/// <param name="Embedding">The vector representation of this chunk's text.</param>
/// <param name="DocumentId">The identifier of the source document this chunk belongs to.</param>
/// <param name="Metadata">Arbitrary metadata attached to this chunk.</param>
public partial record RagChunk(
    string Id,
    string Text,
    float[] Embedding,
    string DocumentId,
    IReadOnlyDictionary<string, string>? Metadata);
