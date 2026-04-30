namespace NetIndex.Core.Abstractions;

/// <summary>
/// Represents a chunked text segment from a source document, ready for embedding and storage.
/// </summary>
/// <remarks>
/// Canonical noun #3 in NOUNS.md.
/// 
/// Lifecycle:
/// <list type="number">
///   <item><term>Create</term><description>Chunking strategy creates <see cref="RagChunk"/> with <c>Embedding = null</c>.</description></item>
///   <item><term>Embed</term><description>Embedding generator sets <see cref="Embedding"/>.</description></item>
///   <item><term>Store</term><description>Vector store persists the chunk with its embedding.</description></item>
/// </list>
/// </remarks>
/// <param name="Id">Unique identifier for this chunk.</param>
/// <param name="Text">The text content of this chunk.</param>
/// <param name="Embedding">The vector representation. Null until the embedding generator processes this chunk.</param>
/// <param name="DocumentId">The identifier of the source document this chunk belongs to.</param>
/// <param name="Metadata">Arbitrary metadata attached to this chunk.</param>
public record RagChunk(
    string Id,
    string Text,
    float[]? Embedding,
    string DocumentId,
    IReadOnlyDictionary<string, string>? Metadata);
