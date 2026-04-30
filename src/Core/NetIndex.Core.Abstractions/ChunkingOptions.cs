namespace NetIndex.Core.Abstractions;

/// <summary>
/// Configuration options for text chunking strategies.
/// </summary>
/// <param name="ChunkSize">Target number of tokens or characters per chunk.</param>
/// <param name="ChunkOverlap">Number of tokens or characters to overlap between consecutive chunks.</param>
/// <param name="Separator">String or regex pattern used to split text before chunking.</param>
public partial record ChunkingOptions(
    int ChunkSize,
    int ChunkOverlap,
    string Separator);
