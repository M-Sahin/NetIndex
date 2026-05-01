using System.Security.Cryptography;
using System.Text;
using NetIndex.Core.Abstractions;

namespace NetIndex.Core;

/// <summary>
/// Deterministic in-memory embedding generator used by zero-config setup.
/// </summary>
public sealed class InMemoryEmbeddingGenerator : IEmbeddingGenerator
{
    /// <inheritdoc />
    public int Dimensions { get; } = 384;

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
