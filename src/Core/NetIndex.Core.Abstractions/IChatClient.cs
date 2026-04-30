using System.Threading;
using System.Threading.Tasks;

namespace NetIndex.Core.Abstractions;

/// <summary>
/// Abstracts LLM-powered answer synthesis and chat generation.
/// </summary>
/// <remarks>
/// Canonical noun #9 (Provider) in NOUNS.md.
/// 
/// In V1, implementations may wrap Ollama or OpenAI-compatible endpoints.
/// The pipeline provides retrieved chunks as context; the chat client handles
/// prompt assembly and response generation.
/// </remarks>
public interface IChatClient
{
    /// <summary>
    /// Generates a complete answer for the given prompt and context in a single response.
    /// </summary>
    /// <param name="prompt">The user's question or instruction.</param>
    /// <param name="context">Retrieved document chunks to include as context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated answer text.</returns>
    Task<string> GenerateAsync(
        string prompt,
        IEnumerable<RagChunk> context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams generated tokens for the given prompt and context.
    /// </summary>
    /// <param name="prompt">The user's question or instruction.</param>
    /// <param name="context">Retrieved document chunks to include as context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A stream of <see cref="GenerationChunk"/> tokens. The final chunk has <c>IsComplete = true</c>.</returns>
    IAsyncEnumerable<GenerationChunk> GenerateStreamingAsync(
        string prompt,
        IEnumerable<RagChunk> context,
        CancellationToken cancellationToken = default);
}
