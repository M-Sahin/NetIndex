using NetIndex.Core.Abstractions;

namespace NetIndex.Evaluation.Tests.Metrics;

public class FaithfulnessMetricsTests
{
    private static readonly IReadOnlyDictionary<string, string> NoMetadata =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static RagChunk Chunk(string id, string text) =>
        new(id, text, Embedding: null, DocumentId: id, Metadata: NoMetadata);

    // ── Score cases ──

    [Fact]
    public void Compute_FullyGroundedAnswer_ScoresOne()
    {
        RagChunk[] context = [Chunk("c1", "cosine similarity vector embedding ranking order")];
        // Answer uses only tokens from context
        var result = FaithfulnessMetrics.Compute("cosine similarity vector", context);

        Assert.Equal(1.0, result.Score, precision: 10);
        Assert.Empty(result.UnsupportedTokens);
    }

    [Fact]
    public void Compute_PartiallyUnsupportedAnswer_ScoresStrictlyBetweenZeroAndOne()
    {
        RagChunk[] context = [Chunk("c1", "vector embedding cosine")];
        // "vector" and "cosine" are in context; "hallucinated" is not
        var result = FaithfulnessMetrics.Compute("vector cosine hallucinated", context);

        Assert.True(result.Score > 0.0, $"Expected score > 0 but got {result.Score}");
        Assert.True(result.Score < 1.0, $"Expected score < 1 but got {result.Score}");
        Assert.Contains("hallucinated", result.UnsupportedTokens, StringComparer.Ordinal);
    }

    [Fact]
    public void Compute_WhollyUnsupportedAnswer_ScoresZero()
    {
        RagChunk[] context = [Chunk("c1", "vector embedding cosine")];
        var result = FaithfulnessMetrics.Compute("unrelated content here", context);

        Assert.Equal(0.0, result.Score, precision: 10);
        Assert.NotEmpty(result.UnsupportedTokens);
    }

    [Fact]
    public void Compute_EmptyAnswer_ScoresZero()
    {
        RagChunk[] context = [Chunk("c1", "some content here")];
        var result = FaithfulnessMetrics.Compute(string.Empty, context);

        Assert.Equal(0.0, result.Score, precision: 10);
        Assert.Empty(result.UnsupportedTokens);
    }

    [Fact]
    public void Compute_WhitespaceOnlyAnswer_ScoresZero()
    {
        RagChunk[] context = [Chunk("c1", "some content here")];
        var result = FaithfulnessMetrics.Compute("   \t\n  ", context);

        Assert.Equal(0.0, result.Score, precision: 10);
        Assert.Empty(result.UnsupportedTokens);
    }

    [Fact]
    public void Compute_EmptyContextList_ScoresZero()
    {
        var result = FaithfulnessMetrics.Compute("some answer about anything", Array.Empty<RagChunk>());

        Assert.Equal(0.0, result.Score, precision: 10);
        Assert.Empty(result.UnsupportedTokens);
    }

    [Fact]
    public void Compute_AnswerConsistsOnlyOfStopwords_ScoresZero()
    {
        RagChunk[] context = [Chunk("c1", "real content about vectors")];
        // All tokens below are stopwords; none are content tokens
        var result = FaithfulnessMetrics.Compute("the is a an and or but", context);

        Assert.Equal(0.0, result.Score, precision: 10);
    }

    // ── Normalization ──

    [Fact]
    public void Compute_TokenMatchIsCaseInsensitive()
    {
        RagChunk[] context = [Chunk("c1", "Vector Embedding Cosine")];
        // Answer uses different casing; should still match
        var result = FaithfulnessMetrics.Compute("VECTOR EMBEDDING COSINE", context);

        Assert.Equal(1.0, result.Score, precision: 10);
    }

    [Fact]
    public void Compute_PunctuationIsStrippedBeforeMatching()
    {
        RagChunk[] context = [Chunk("c1", "vector embedding ranking")];
        // Answer has punctuation around tokens; should match after stripping
        var result = FaithfulnessMetrics.Compute("vector, embedding. ranking!", context);

        Assert.Equal(1.0, result.Score, precision: 10);
    }

    [Fact]
    public void Compute_StopwordsExcludedFromAnswerTokenCount()
    {
        // "the" is a stopword; only "vector" is a content token
        RagChunk[] context = [Chunk("c1", "vector data")];
        var result = FaithfulnessMetrics.Compute("the vector", context);

        // "the" excluded; only "vector" counted → 1/1 = 1.0
        Assert.Equal(1.0, result.Score, precision: 10);
    }

    [Fact]
    public void Compute_DuplicateAnswerTokensCountedOnce()
    {
        RagChunk[] context = [Chunk("c1", "vector embedding")];
        // "vector" repeated in answer; unique token count = 1
        var result = FaithfulnessMetrics.Compute("vector vector vector", context);

        // 1 unique token, all supported → 1.0
        Assert.Equal(1.0, result.Score, precision: 10);
    }

    // ── Error cases ──

    [Fact]
    public void Compute_NullAnswer_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FaithfulnessMetrics.Compute(null!, new[] { Chunk("c1", "text") }));
    }

    [Fact]
    public void Compute_NullContextList_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FaithfulnessMetrics.Compute("answer", null!));
    }

    [Fact]
    public void Compute_DuplicateContextChunkIds_Throws()
    {
        var duplicate = Chunk("c1", "content");
        var exception = Assert.Throws<ArgumentException>(() =>
            FaithfulnessMetrics.Compute("answer", new[] { duplicate, duplicate }));

        Assert.Contains("c1", exception.Message, StringComparison.Ordinal);
    }

    // ── MeanFaithfulness ──

    [Fact]
    public void MeanFaithfulness_EmptySet_Throws()
    {
        Assert.Throws<ArgumentException>(() => FaithfulnessMetrics.MeanFaithfulness([]));
    }

    [Fact]
    public void MeanFaithfulness_MultipleScores_ReturnsArithmeticMean()
    {
        var mean = FaithfulnessMetrics.MeanFaithfulness([0.8, 1.0, 0.6]);

        Assert.Equal(0.8, mean, precision: 10);
    }

    [Fact]
    public void MeanFaithfulness_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => FaithfulnessMetrics.MeanFaithfulness(null!));
    }

    // ── Diagnostic list is bounded ──

    [Fact]
    public void Compute_ManyUnsupportedTokens_DiagnosticsListIsBounded()
    {
        RagChunk[] context = [Chunk("c1", "one single word")];
        // 20 unique unsupported content tokens
        var answer = "apple banana cherry date elderberry fig grape honeydew " +
                     "iceberg jackfruit kiwi lemon mango nectarine orange papaya " +
                     "quince raspberry strawberry";

        var result = FaithfulnessMetrics.Compute(answer, context);

        Assert.True(result.UnsupportedTokens.Count <= 10,
            $"Expected at most 10 diagnostics but got {result.UnsupportedTokens.Count}");
    }
}
