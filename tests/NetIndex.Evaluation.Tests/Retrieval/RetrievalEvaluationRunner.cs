using System.Globalization;
using NetIndex.Core.Abstractions;
using NetIndex.Evaluation.Tests.Fixtures;
using NetIndex.Evaluation.Tests.Metrics;
using NetIndex.Evaluation.Tests.TestSupport;

namespace NetIndex.Evaluation.Tests.Retrieval;

/// <summary>
/// Per-query retrieval outcome: the ranked chunk Ids actually returned by the pipeline, their
/// scores, and the metrics computed against the fixture's ground-truth relevance judgments.
/// </summary>
internal sealed record RetrievalQueryResult(
    string QueryId,
    IReadOnlyList<string> RankedChunkIds,
    IReadOnlyList<float> Scores,
    double ReciprocalRank,
    double NdcgAtK);

/// <summary>
/// Aggregate retrieval evaluation outcome across every query in a dataset.
/// </summary>
internal sealed record RetrievalEvaluationReport(
    IReadOnlyList<RetrievalQueryResult> QueryResults,
    double MeanReciprocalRank,
    double MeanNdcgAtK);

/// <summary>
/// Drives a real <see cref="INetIndexPipeline"/> through ingest and query for a retrieval
/// evaluation dataset, scoring the actual ranking against committed relevance judgments.
/// Identity is read from <c>SearchResult.Item.Id</c> — never <c>SearchResult.DocumentId</c>,
/// which the in-memory store populates with the chunk Id rather than the source document Id.
/// </summary>
internal sealed class RetrievalEvaluationRunner(INetIndexPipeline pipeline)
{
    public async Task IngestAsync(
        IReadOnlyList<RetrievalEvaluationDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        foreach (var document in documents)
        {
            await pipeline.IngestAsync(new EvaluationDocument(document.Id, document.Content), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<RetrievalEvaluationReport> EvaluateAsync(
        RetrievalEvaluationDataset dataset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        if (dataset.Queries is null || dataset.Queries.Count == 0)
        {
            throw new InvalidOperationException("Cannot evaluate retrieval over a dataset with zero queries.");
        }

        var queryResults = new List<RetrievalQueryResult>();
        foreach (var query in dataset.Queries)
        {
            var judgments = RetrievalEvaluationDatasetLoader.BuildJudgmentLookup(query);

            var rankedChunkIds = new List<string>();
            var scores = new List<float>();
            await foreach (var result in pipeline.QueryAsync(query.Text, cancellationToken)
                .WithCancellation(cancellationToken))
            {
                rankedChunkIds.Add(result.Item.Id);
                scores.Add(result.Score);
            }

            var reciprocalRank = RetrievalMetrics.ReciprocalRank(rankedChunkIds, judgments);
            var ndcgAtK = RetrievalMetrics.NdcgAtK(rankedChunkIds, judgments, dataset.TopK);

            queryResults.Add(new RetrievalQueryResult(query.Id, rankedChunkIds, scores, reciprocalRank, ndcgAtK));
        }

        var meanReciprocalRank = RetrievalMetrics.MeanReciprocalRank(
            queryResults.Select(r => r.ReciprocalRank).ToList());
        var meanNdcg = RetrievalMetrics.MeanNdcg(
            queryResults.Select(r => r.NdcgAtK).ToList());

        return new RetrievalEvaluationReport(queryResults, meanReciprocalRank, meanNdcg);
    }
}

/// <summary>
/// Enforces a retrieval evaluation report against committed thresholds, failing with the
/// expected and actual values rather than a bare boolean.
/// </summary>
internal static class RetrievalThresholdGate
{
    public static void AssertMeetsThresholds(RetrievalEvaluationReport report, RetrievalEvaluationThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(thresholds);

        if (report.QueryResults.Count == 0)
        {
            throw new InvalidOperationException("Retrieval quality gate cannot pass with zero evaluated queries.");
        }

        if (report.MeanReciprocalRank < thresholds.MeanReciprocalRank)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"Mean Reciprocal Rank {report.MeanReciprocalRank:F4} is below threshold {thresholds.MeanReciprocalRank:F4}."));
        }

        if (report.MeanNdcgAtK < thresholds.MeanNdcgAtK)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"Mean NDCG@k {report.MeanNdcgAtK:F4} is below threshold {thresholds.MeanNdcgAtK:F4}."));
        }
    }
}
