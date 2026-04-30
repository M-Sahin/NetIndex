namespace NetIndex.Core.Abstractions;

/// <summary>
/// Reason why an LLM generation stream terminated.
/// </summary>
public enum FinishReason
{
    /// <summary>
    /// The model generated a natural stopping point (e.g., end of sentence, stop token).
    /// </summary>
    Stop,

    /// <summary>
    /// The generation was stopped because the maximum token limit was reached.
    /// </summary>
    Length,

    /// <summary>
    /// The generation was stopped due to a content filter or safety policy.
    /// </summary>
    ContentFilter,

    /// <summary>
    /// The generation was stopped because the caller cancelled the operation.
    /// </summary>
    Cancelled,

    /// <summary>
    /// The generation was stopped due to an error in the provider.
    /// </summary>
    Error
}
