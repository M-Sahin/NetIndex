using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Options;

namespace NetIndex.Ingestion.Strategies;

/// <summary>
/// Splits text at semantic boundaries (topic changes) using embedding similarity comparison.
/// </summary>
/// <remarks>
/// Sentences are grouped into candidate chunks, embeddings are computed for each candidate,
/// and splits occur where consecutive embeddings fall below a similarity threshold.
/// </remarks>
/// TODO: Verify semantic boundary detection with a real IEmbeddingGenerator (Ollama/OpenAI) when
/// available in Story 3-4/3-7. The FakeEmbeddingGenerator produces unit-length deterministic
/// vectors, so cosine similarity between any two inputs is ~1.0 — boundaries are never detected
/// in tests. The structural tests here validate the algorithm's shape; integration tests with
/// real embeddings will validate semantic detection quality.
public sealed class SemanticChunkingStrategy : IChunkingStrategy
{
    private const int CharsPerToken = 4;
    private const float SimilarityThreshold = 0.7f;
    private static readonly Regex SentenceSplitter = new(@"(?<=[.!?])\s+(?=[A-Z])", RegexOptions.Compiled);

    private readonly IEmbeddingGenerator _embeddingGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="SemanticChunkingStrategy"/> class.
    /// </summary>
    /// <param name="embeddingGenerator">The embedding generator for computing semantic similarity.</param>
    /// <param name="configuration">The chunking configuration.</param>
    public SemanticChunkingStrategy(
        IEmbeddingGenerator embeddingGenerator,
        IOptions<ChunkingConfiguration> configuration)
    {
        ArgumentNullException.ThrowIfNull(embeddingGenerator);
        ArgumentNullException.ThrowIfNull(configuration);
        _embeddingGenerator = embeddingGenerator;
        // Configuration is read by the DI factory; chunk sizes come from ChunkingOptions at runtime.
        _ = configuration.Value;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RagChunk>> ChunkAsync(string text, ChunkingOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);

        if (options.ChunkSize <= 0)
        {
            throw new ArgumentException("ChunkSize must be greater than zero.", nameof(options));
        }

        var maxChars = TokensToChars(options.ChunkSize);

        // Split into sentences
        var sentences = SentenceSplitter.Split(text)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();

        if (sentences.Length == 0)
        {
            return Array.Empty<RagChunk>();
        }

        // Group sentences into candidate chunks of ~200 characters
        var candidates = new List<string>();
        var currentCandidate = new System.Text.StringBuilder();

        foreach (var sentence in sentences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (currentCandidate.Length + sentence.Length > maxChars && currentCandidate.Length > 0)
            {
                candidates.Add(currentCandidate.ToString().Trim());
                currentCandidate.Clear();
            }

            currentCandidate.Append(sentence);
            currentCandidate.Append(' ');
        }

        if (currentCandidate.Length > 0)
        {
            candidates.Add(currentCandidate.ToString().Trim());
        }

        if (candidates.Count <= 1)
        {
            // Single candidate — no semantic boundary to detect
            return new[]
            {
                new RagChunk("chunk_0", candidates.Count > 0 ? candidates[0] : text.Trim(), null, "pending", null)
            };
        }

        // Compute embeddings for all candidates
        var embeddings = await _embeddingGenerator.GenerateBatchAsync(candidates, cancellationToken).ConfigureAwait(false);

        // Detect semantic boundaries
        var boundaries = new List<int> { 0 };

        for (var i = 1; i < candidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var similarity = CosineSimilarity(embeddings[i - 1], embeddings[i]);
            if (similarity < SimilarityThreshold)
            {
                boundaries.Add(i);
            }
        }

        boundaries.Add(candidates.Count);

        // Build chunks from boundary groups
        var chunks = new List<RagChunk>();
        var chunkIndex = 0;

        for (var b = 0; b < boundaries.Count - 1; b++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var start = boundaries[b];
            var end = boundaries[b + 1];

            var combinedText = string.Join(" ", candidates[start..end]).Trim();
            if (combinedText.Length == 0)
            {
                continue;
            }

            chunks.Add(new RagChunk($"chunk_{chunkIndex}", combinedText, null, "pending", null));
            chunkIndex++;
        }

        return chunks;
    }

    /// <summary>
    /// Computes the cosine similarity between two embedding vectors.
    /// </summary>
    /// <param name="a">The first embedding vector.</param>
    /// <param name="b">The second embedding vector.</param>
    /// <returns>The cosine similarity (range -1 to 1).</returns>
    /// <exception cref="ArgumentException">Thrown when embeddings have mismatched or zero length.</exception>
    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length == 0)
        {
            throw new ArgumentException("Embedding vector must not be empty.", nameof(a));
        }

        if (a.Length != b.Length)
        {
            throw new ArgumentException($"Embedding dimension mismatch: a has {a.Length}, b has {b.Length}.", nameof(b));
        }

        float dotProduct = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var magnitude = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return magnitude == 0 ? 0f : dotProduct / magnitude;
    }

    private static int TokensToChars(int tokens) => tokens * CharsPerToken;
}