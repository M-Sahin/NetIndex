namespace NetIndex.Evaluation.Tests.Metrics;

public class RetrievalMetricsTests
{
    [Fact]
    public void ReciprocalRank_FirstResultRelevant_ReturnsOne()
    {
        var judgments = new Dictionary<string, int> { ["c1"] = 2 };

        var rr = RetrievalMetrics.ReciprocalRank(["c1", "c2", "c3"], judgments);

        Assert.Equal(1.0, rr, precision: 10);
    }

    [Fact]
    public void ReciprocalRank_RelevantAtRankTwo_ReturnsHalf()
    {
        var judgments = new Dictionary<string, int> { ["c2"] = 1 };

        var rr = RetrievalMetrics.ReciprocalRank(["c1", "c2", "c3"], judgments);

        Assert.Equal(0.5, rr, precision: 10);
    }

    [Fact]
    public void ReciprocalRank_NoRelevantResult_ReturnsZero()
    {
        var judgments = new Dictionary<string, int> { ["other"] = 3 };

        var rr = RetrievalMetrics.ReciprocalRank(["c1", "c2"], judgments);

        Assert.Equal(0.0, rr, precision: 10);
    }

    [Fact]
    public void ReciprocalRank_DuplicateRankedIds_Throws()
    {
        var judgments = new Dictionary<string, int> { ["c1"] = 1 };

        Assert.Throws<ArgumentException>(() => RetrievalMetrics.ReciprocalRank(["c1", "c1"], judgments));
    }

    [Fact]
    public void MeanReciprocalRank_MultipleQueries_ReturnsAverage()
    {
        var mrr = RetrievalMetrics.MeanReciprocalRank([1.0, 0.5, 0.0]);

        Assert.Equal(0.5, mrr, precision: 10);
    }

    [Fact]
    public void MeanReciprocalRank_EmptyCollection_Throws()
    {
        Assert.Throws<ArgumentException>(() => RetrievalMetrics.MeanReciprocalRank([]));
    }

    [Fact]
    public void NdcgAtK_PerfectRanking_ReturnsOne()
    {
        var judgments = new Dictionary<string, int> { ["a"] = 3, ["b"] = 1 };

        var ndcg = RetrievalMetrics.NdcgAtK(["a", "b"], judgments, k: 2);

        Assert.Equal(1.0, ndcg, precision: 10);
    }

    [Fact]
    public void NdcgAtK_ImperfectRanking_PenalizesMisorderedResults()
    {
        var judgments = new Dictionary<string, int> { ["a"] = 3, ["b"] = 1 };

        var ndcg = RetrievalMetrics.NdcgAtK(["b", "a"], judgments, k: 2);

        Assert.Equal(0.7098097414, ndcg, precision: 8);
    }

    [Fact]
    public void NdcgAtK_TruncatedAtK_IgnoresResultsBeyondK()
    {
        var judgments = new Dictionary<string, int> { ["a"] = 3, ["b"] = 1 };

        var ndcg = RetrievalMetrics.NdcgAtK(["a", "b", "unjudged"], judgments, k: 1);

        Assert.Equal(1.0, ndcg, precision: 10);
    }

    [Fact]
    public void NdcgAtK_NoRelevantJudgments_ReturnsZero()
    {
        var ndcg = RetrievalMetrics.NdcgAtK(["x", "y"], new Dictionary<string, int>(), k: 2);

        Assert.Equal(0.0, ndcg, precision: 10);
    }

    [Fact]
    public void NdcgAtK_GradedRelevanceWithUnjudgedChunk_TreatsUnjudgedAsZeroRelevance()
    {
        var judgments = new Dictionary<string, int> { ["a"] = 2 };

        var ndcg = RetrievalMetrics.NdcgAtK(["a", "unjudged"], judgments, k: 2);

        Assert.Equal(1.0, ndcg, precision: 10);
    }

    [Fact]
    public void NdcgAtK_DuplicateRankedIds_Throws()
    {
        var judgments = new Dictionary<string, int> { ["a"] = 1 };

        Assert.Throws<ArgumentException>(() => RetrievalMetrics.NdcgAtK(["a", "a"], judgments, k: 2));
    }

    [Fact]
    public void NdcgAtK_NonPositiveK_Throws()
    {
        var judgments = new Dictionary<string, int> { ["a"] = 1 };

        Assert.Throws<ArgumentOutOfRangeException>(() => RetrievalMetrics.NdcgAtK(["a"], judgments, k: 0));
    }

    [Fact]
    public void NdcgAtK_OutOfRangeGrade_Throws()
    {
        var judgments = new Dictionary<string, int> { ["a"] = 4 };

        Assert.Throws<ArgumentOutOfRangeException>(() => RetrievalMetrics.NdcgAtK(["a"], judgments, k: 1));
    }

    [Fact]
    public void MeanNdcg_MultipleQueries_ReturnsAverage()
    {
        var mean = RetrievalMetrics.MeanNdcg([1.0, 0.5]);

        Assert.Equal(0.75, mean, precision: 10);
    }

    [Fact]
    public void MeanNdcg_EmptyCollection_Throws()
    {
        Assert.Throws<ArgumentException>(() => RetrievalMetrics.MeanNdcg([]));
    }
}
