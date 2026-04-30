namespace NetIndex.Core.Abstractions;

/// <summary>
/// Represents a single token or segment emitted during LLM streaming generation.
/// </summary>
/// <remarks>
/// Canonical noun #7 in NOUNS.md.
/// 
/// <para>The final chunk in a stream has <see cref="IsComplete"/> set to <c>true</c> and
/// <see cref="FinishReason"/> indicates why generation stopped.</para>
/// </remarks>
/// <param name="Text">The text fragment produced by this chunk.</param>
/// <param name="IsComplete">True if this is the final chunk in the generation stream.</param>
/// <param name="FinishReason">Reason for stream termination. Only meaningful on the final chunk.</param>
public record GenerationChunk(
    string Text,
    bool IsComplete,
    FinishReason FinishReason);
