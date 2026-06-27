using System.Security.Cryptography;
using System.Text;
using NetIndex.Core.Abstractions;

namespace NetIndex.Evaluation.Tests.TestSupport;

/// <summary>
/// Deterministic, offline lexical embedding generator using the hashing trick: each lowercase
/// token is hashed (SHA-256 over its UTF-8 bytes) into one of <see cref="Dimensions"/> buckets,
/// bucket counts are accumulated, and the resulting vector is L2-normalized.
/// </summary>
/// <remarks>
/// Unlike a whole-string hash, token-level hashing gives lexically similar texts a meaningfully
/// higher cosine similarity than unrelated texts, which retrieval-quality tests depend on. Never
/// uses <c>string.GetHashCode()</c> or <c>HashCode</c> — both are process-randomized per run and
/// would make rankings non-reproducible across test runs.
/// </remarks>
internal sealed class DeterministicTokenEmbeddingGenerator : IEmbeddingGenerator
{
    // Closed-class English stopwords carry almost no topical signal but appear in nearly every
    // text; under plain term-frequency cosine they add noise that can outweigh the one or two
    // real content words a short query shares with its true match. Filtering them is a standard,
    // model-agnostic lexical IR step (not a fixture-specific tuning hack).
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "is", "are", "was", "were", "be", "been", "being", "to", "of", "in", "on", "for",
        "and", "or", "but", "with", "by", "from", "this", "that", "these", "those", "it", "its", "as", "at",
        "can", "what", "while", "i", "you", "he", "she", "they", "we", "do", "does", "did", "have", "has",
        "had", "not", "no", "so", "if", "then", "than", "which", "who", "whom", "when", "where", "why",
        "how", "s", "t", "re", "ve", "ll", "d", "m", "there", "here", "all", "any", "each", "such", "into",
        "before", "after", "up", "down", "out", "about", "between", "through", "during",
    };

    public DeterministicTokenEmbeddingGenerator(int dimensions)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
        Dimensions = dimensions;
    }

    public int Dimensions { get; }

    public Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Embed(text));
    }

    public async Task<float[][]> GenerateBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var results = new List<float[]>();
        foreach (var text in texts)
        {
            results.Add(await GenerateAsync(text, cancellationToken).ConfigureAwait(false));
        }

        return results.ToArray();
    }

    private float[] Embed(string text)
    {
        var buckets = new double[Dimensions];
        foreach (var token in Tokenize(text))
        {
            buckets[HashToBucket(token)] += 1.0;
        }

        var vector = new float[Dimensions];
        var norm = Math.Sqrt(buckets.Sum(value => value * value));
        if (norm == 0.0)
        {
            return vector;
        }

        for (var i = 0; i < Dimensions; i++)
        {
            vector[i] = (float)(buckets[i] / norm);
        }

        return vector;
    }

    private int HashToBucket(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var bucket = ((uint)hash[0] << 24) | ((uint)hash[1] << 16) | ((uint)hash[2] << 8) | hash[3];
        return (int)(bucket % (uint)Dimensions);
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        foreach (var token in SplitIntoLowercaseTokens(text))
        {
            if (!StopWords.Contains(token))
            {
                yield return token;
            }
        }
    }

    private static IEnumerable<string> SplitIntoLowercaseTokens(string text)
    {
        var builder = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (builder.Length > 0)
            {
                yield return builder.ToString();
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }
}
