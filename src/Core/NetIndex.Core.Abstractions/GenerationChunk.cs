namespace NetIndex.Core.Abstractions;

/// <summary>
/// Represents a single token or segment emitted during LLM streaming generation.
/// </summary>
/// <remarks>
/// Canonical noun #7 in NOUNS.md. This is a forward declaration; full definition moves to story 1.3.
/// </remarks>
/// <param name="Text">The text fragment produced by this chunk.</param>
/// <param name="IsComplete">True if this is the final chunk in the generation stream.</param>
/// <param name="Reason">Optional reason for stream termination (e.g., "length", "stop", "error").</param>
public partial record GenerationChunk(
    string Text,
    bool IsComplete,
    string? Reason);
