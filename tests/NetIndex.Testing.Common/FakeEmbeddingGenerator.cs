using System.Security.Cryptography;
using System.Text;
using NetIndex.Core.Abstractions;

namespace NetIndex.Testing.Common;

/// <summary>
/// Deterministic, no-network <see cref="IEmbeddingGenerator"/> for tests.
/// </summary>
/// <remarks>
/// Produces reproducible unit-length vectors from input text. Same text always yields
/// the same embedding. Configurable output dimensions via <see cref="Dimensions"/>.
/// </remarks>
public sealed class FakeEmbeddingGenerator : IEmbeddingGenerator
{
    /// <summary>
    /// Gets the number of dimensions this generator produces.
    /// </summary>
    public int Dimensions { get; }

    /// <summary>
    /// Initializes a new instance producing 384-dimensional embeddings.
    /// </summary>
    public FakeEmbeddingGenerator() : this(384) { }

    /// <summary>
    /// Initializes a new instance producing <paramref name="dimensions"/>-dimensional embeddings.
    /// </summary>
    /// <param name="dimensions">The output dimension count. Must be greater than zero.</param>
    public FakeEmbeddingGenerator(int dimensions)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
        Dimensions = dimensions;
    }

    /// <inheritdoc />
    public Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(text);
        return Task.FromResult(CreateEmbedding(text));
    }

    /// <inheritdoc />
    public async Task<float[][]> GenerateBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var embeddings = new List<float[]>();
        foreach (var text in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            embeddings.Add(await GenerateAsync(text, cancellationToken).ConfigureAwait(false));
        }

        return embeddings.ToArray();
    }

    private float[] CreateEmbedding(string text)
    {
        var vector = new float[Dimensions];
        double magnitudeSquared = 0;

        for (var index = 0; index < vector.Length; index++)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{Dimensions}:{index}:{text}"));
            var rawValue = BitConverter.ToUInt32(hash, 0);
            var component = (rawValue / (float)uint.MaxValue) * 2f - 1f;
            vector[index] = component;
            magnitudeSquared += component * component;
        }

        if (magnitudeSquared <= 0)
        {
            vector[0] = 1f;
            return vector;
        }

        var magnitude = (float)Math.Sqrt(magnitudeSquared);
        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] /= magnitude;
        }

        return vector;
    }
}
