using System.Threading;
using System.Threading.Tasks;

namespace NetIndex.Core.Abstractions;

/// <summary>
/// Generates vector embeddings for text inputs.
/// </summary>
/// <remarks>
/// Canonical noun #9 (Provider) in NOUNS.md.
/// 
/// Implementations may wrap Ollama, OpenAI, Azure OpenAI, or any other embedding provider.
/// The <see cref="Dimensions"/> property is used by the pipeline to validate dimension
/// consistency at startup (fail-fast, FR11).
/// </remarks>
public interface IEmbeddingGenerator
{
    /// <summary>
    /// Gets the number of dimensions in vectors produced by this generator.
    /// </summary>
    /// <remarks>
    /// Used for dimension mismatch validation at pipeline startup.
    /// Must match the dimensions configured on the vector store.
    /// </remarks>
    int Dimensions { get; }

    /// <summary>
    /// Generates an embedding vector for the given text.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A vector of length <see cref="Dimensions"/>.</returns>
    Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embedding vectors for multiple texts in a single batch operation.
    /// </summary>
    /// <param name="texts">The texts to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An array of vectors, each of length <see cref="Dimensions"/>.</returns>
    Task<float[][]> GenerateBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);
}
