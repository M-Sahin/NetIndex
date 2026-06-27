using System.Text;
using NetIndex.Core.Abstractions;

namespace NetIndex.Evaluation.Tests.Metrics;

/// <summary>
/// Deterministic, offline answer-faithfulness metric: token-level groundedness of a generated answer
/// against the retrieved context chunks actually passed to the chat client.
/// </summary>
/// <remarks>
/// Faithfulness = supportedUniqueAnswerContentTokens / uniqueAnswerContentTokens.
/// A token is supported when its normalized form appears in at least one context chunk.
/// Content tokens are lowercase, alphanumeric, punctuation-stripped, and stopword-filtered.
/// </remarks>
internal static class FaithfulnessMetrics
{
    private const int MaxUnsupportedDiagnostics = 10;

    // Closed-class English stopwords: same list as DeterministicTokenEmbeddingGenerator so that
    // the tokens the chat client uses in its answer match exactly the tokens the metric checks against.
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "is", "are", "was", "were", "be", "been", "being", "to", "of", "in", "on", "for",
        "and", "or", "but", "with", "by", "from", "this", "that", "these", "those", "it", "its", "as", "at",
        "can", "what", "while", "i", "you", "he", "she", "they", "we", "do", "does", "did", "have", "has",
        "had", "not", "no", "so", "if", "then", "than", "which", "who", "whom", "when", "where", "why",
        "how", "s", "t", "re", "ve", "ll", "d", "m", "there", "here", "all", "any", "each", "such", "into",
        "before", "after", "up", "down", "out", "about", "between", "through", "during",
    };

    /// <summary>
    /// Computes the faithfulness score for a generated <paramref name="answer"/> against
    /// the <paramref name="contextChunks"/> that were supplied to the chat client.
    /// </summary>
    /// <param name="answer">The generated answer text.</param>
    /// <param name="contextChunks">The retrieved chunks passed to the chat client. Must not be null or contain duplicate IDs.</param>
    /// <returns>
    /// A <see cref="FaithfulnessResult"/> with a score in [0.0, 1.0] and a bounded list of
    /// unsupported content tokens for diagnostics.
    /// </returns>
    public static FaithfulnessResult Compute(string answer, IReadOnlyList<RagChunk> contextChunks)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(contextChunks);

        ValidateNoDuplicateChunkIds(contextChunks);

        if (string.IsNullOrWhiteSpace(answer))
        {
            return new FaithfulnessResult(0.0, []);
        }

        if (contextChunks.Count == 0)
        {
            return new FaithfulnessResult(0.0, []);
        }

        var answerTokens = UniqueContentTokens(answer);
        if (answerTokens.Count == 0)
        {
            return new FaithfulnessResult(0.0, []);
        }

        var contextTokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in contextChunks)
        {
            foreach (var token in Tokenize(chunk.Text))
            {
                contextTokens.Add(token);
            }
        }

        var unsupported = new List<string>();
        var supportedCount = 0;
        foreach (var token in answerTokens)
        {
            if (contextTokens.Contains(token))
            {
                supportedCount++;
            }
            else if (unsupported.Count < MaxUnsupportedDiagnostics)
            {
                unsupported.Add(token);
            }
        }

        return new FaithfulnessResult((double)supportedCount / answerTokens.Count, unsupported.AsReadOnly());
    }

    /// <summary>
    /// Arithmetic mean of per-query faithfulness scores. Rejects an empty set rather than returning a vacuous pass.
    /// </summary>
    public static double MeanFaithfulness(IReadOnlyCollection<double> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        if (scores.Count == 0)
        {
            throw new ArgumentException("Cannot compute mean faithfulness over an empty query set.", nameof(scores));
        }

        return scores.Average();
    }

    private static HashSet<string> UniqueContentTokens(string text)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in Tokenize(text))
        {
            tokens.Add(token);
        }

        return tokens;
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

    private static void ValidateNoDuplicateChunkIds(IReadOnlyList<RagChunk> contextChunks)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in contextChunks)
        {
            ArgumentNullException.ThrowIfNull(chunk);
            if (!seen.Add(chunk.Id))
            {
                throw new ArgumentException(
                    $"Context chunk list contains duplicate Id '{chunk.Id}'.", nameof(contextChunks));
            }
        }
    }
}

/// <summary>
/// The faithfulness score for a generated answer together with a bounded diagnostic list of unsupported tokens.
/// </summary>
/// <param name="Score">Groundedness ratio in [0.0, 1.0]: supported unique answer tokens / total unique answer tokens.</param>
/// <param name="UnsupportedTokens">Normalized tokens in the answer that have no match in the context (bounded list for diagnostics).</param>
internal sealed record FaithfulnessResult(double Score, IReadOnlyList<string> UnsupportedTokens);
