using System.Collections.Generic;
using System.ComponentModel;

namespace NetIndex.SemanticKernel;

/// <summary>
/// A single chunk returned by the <c>RetrieveChunks</c> plugin function.
/// </summary>
/// <param name="ChunkId">The identifier of the retrieved chunk.</param>
/// <param name="DocumentId">The identifier of the document the chunk belongs to.</param>
/// <param name="Text">The text content of the chunk.</param>
/// <param name="Score">The relevance score assigned by the retrieval pipeline; higher values indicate stronger relevance.</param>
/// <param name="Metadata">Chunk metadata key-value pairs, or empty if the chunk has no metadata.</param>
public sealed record NetIndexRetrievedChunk(
    [property: Description("The identifier of the retrieved chunk.")]
    string ChunkId,
    [property: Description("The identifier of the document the chunk belongs to.")]
    string DocumentId,
    [property: Description("The text content of the chunk.")]
    string Text,
    [property: Description("The relevance score assigned by the retrieval pipeline; higher values indicate stronger relevance.")]
    float Score,
    [property: Description("Chunk metadata key-value pairs, or an empty object if the chunk has no metadata.")]
    IReadOnlyDictionary<string, string> Metadata);
