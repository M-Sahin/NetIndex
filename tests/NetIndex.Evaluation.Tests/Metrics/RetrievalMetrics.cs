using NetIndex.Evaluation.Tests.Fixtures;

namespace NetIndex.Evaluation.Tests.Metrics;

/// <summary>
/// Pure, deterministic Information Retrieval metrics: Mean Reciprocal Rank (MRR) and
/// Normalized Discounted Cumulative Gain (NDCG@k).
/// </summary>
internal static class RetrievalMetrics
{
    /// <summary>
    /// Reciprocal rank of the first relevant (grade &gt;= 1) result in a one-based ranked list.
    /// Returns 0.0 if no result is relevant.
    /// </summary>
    public static double ReciprocalRank(
        IReadOnlyList<string> rankedChunkIds,
        IReadOnlyDictionary<string, int> judgmentsByChunkId)
    {
        ArgumentNullException.ThrowIfNull(rankedChunkIds);
        ArgumentNullException.ThrowIfNull(judgmentsByChunkId);
        EnsureNoDuplicates(rankedChunkIds);
        EnsureGradesInRange(judgmentsByChunkId);

        for (var rank = 1; rank <= rankedChunkIds.Count; rank++)
        {
            var relevance = judgmentsByChunkId.GetValueOrDefault(rankedChunkIds[rank - 1], RelevanceGrade.Min);
            if (relevance >= 1)
            {
                return 1.0 / rank;
            }
        }

        return 0.0;
    }

    /// <summary>
    /// Arithmetic mean of per-query reciprocal ranks. Rejects an empty query set rather than
    /// returning a vacuous 0.0/NaN pass.
    /// </summary>
    public static double MeanReciprocalRank(IReadOnlyCollection<double> reciprocalRanks)
    {
        ArgumentNullException.ThrowIfNull(reciprocalRanks);
        if (reciprocalRanks.Count == 0)
        {
            throw new ArgumentException("Cannot compute Mean Reciprocal Rank over an empty query set.", nameof(reciprocalRanks));
        }

        return reciprocalRanks.Average();
    }

    /// <summary>
    /// Normalized Discounted Cumulative Gain at rank <paramref name="k"/>.
    /// gain = 2^relevance - 1; discount = 1/log2(rank+1); unjudged chunks have relevance 0.
    /// NDCG@k = DCG@k / IDCG@k, or 0.0 when IDCG@k is 0 (no positive judgments at all).
    /// </summary>
    public static double NdcgAtK(
        IReadOnlyList<string> rankedChunkIds,
        IReadOnlyDictionary<string, int> judgmentsByChunkId,
        int k)
    {
        ArgumentNullException.ThrowIfNull(rankedChunkIds);
        ArgumentNullException.ThrowIfNull(judgmentsByChunkId);
        if (k <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(k), k, "k must be a positive integer.");
        }

        EnsureNoDuplicates(rankedChunkIds);
        EnsureGradesInRange(judgmentsByChunkId);

        // DCG truncates at the actual result count, while IDCG below truncates at the full k over all
        // judged grades. This asymmetry is intentional standard NDCG@k: the ideal ranking is the best
        // possible top-k regardless of how few results were returned, so a short result list is
        // legitimately penalized against the fuller ideal. Do not "fix" by clamping IDCG to the result count.
        var take = Math.Min(k, rankedChunkIds.Count);
        var dcg = 0.0;
        for (var rank = 1; rank <= take; rank++)
        {
            var relevance = judgmentsByChunkId.GetValueOrDefault(rankedChunkIds[rank - 1], RelevanceGrade.Min);
            dcg += Gain(relevance) * Discount(rank);
        }

        var idealGrades = judgmentsByChunkId.Values
            .OrderByDescending(grade => grade)
            .Take(k)
            .ToList();

        var idcg = 0.0;
        for (var rank = 1; rank <= idealGrades.Count; rank++)
        {
            idcg += Gain(idealGrades[rank - 1]) * Discount(rank);
        }

        return idcg == 0.0 ? 0.0 : dcg / idcg;
    }

    /// <summary>
    /// Arithmetic mean of per-query NDCG@k values. Rejects an empty query set.
    /// </summary>
    public static double MeanNdcg(IReadOnlyCollection<double> ndcgValues)
    {
        ArgumentNullException.ThrowIfNull(ndcgValues);
        if (ndcgValues.Count == 0)
        {
            throw new ArgumentException("Cannot compute mean NDCG over an empty query set.", nameof(ndcgValues));
        }

        return ndcgValues.Average();
    }

    private static double Gain(int relevance) => Math.Pow(2, relevance) - 1;

    private static double Discount(int oneBasedRank) => 1.0 / Math.Log2(oneBasedRank + 1);

    private static void EnsureNoDuplicates(IReadOnlyList<string> rankedChunkIds)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in rankedChunkIds)
        {
            if (!seen.Add(id))
            {
                throw new ArgumentException($"Ranked result list contains duplicate chunk Id '{id}'.", nameof(rankedChunkIds));
            }
        }
    }

    private static void EnsureGradesInRange(IReadOnlyDictionary<string, int> judgmentsByChunkId)
    {
        foreach (var (chunkId, grade) in judgmentsByChunkId)
        {
            if (grade < RelevanceGrade.Min || grade > RelevanceGrade.Max)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(judgmentsByChunkId), grade, $"Judgment for chunk '{chunkId}' has grade {grade}, outside [{RelevanceGrade.Min}, {RelevanceGrade.Max}].");
            }
        }
    }
}
