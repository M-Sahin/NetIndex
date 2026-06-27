using System.Globalization;
using NetIndex.Core.Abstractions;
using NetIndex.Evaluation.Tests.Fixtures;
using NetIndex.Evaluation.Tests.Metrics;
using NetIndex.Evaluation.Tests.TestSupport;

namespace NetIndex.Evaluation.Tests.Faithfulness;

/// <summary>
/// Per-query faithfulness outcome: the generated answer length, the context chunks passed to the
/// chat client, the faithfulness score, and a bounded list of unsupported tokens for diagnostics.
/// </summary>
internal sealed record FaithfulnessQueryResult(
    string QueryId,
    int AnswerContentLength,
    IReadOnlyList<string> ExpectedContextChunkIds,
    IReadOnlyList<string> ContextChunkIds,
    IReadOnlyList<string> MissingExpectedContextChunkIds,
    double FaithfulnessScore,
    IReadOnlyList<string> UnsupportedTokens);

/// <summary>
/// Aggregate faithfulness evaluation outcome across every faithfulness query in a dataset.
/// </summary>
internal sealed record FaithfulnessEvaluationReport(
    IReadOnlyList<FaithfulnessQueryResult> QueryResults,
    double MeanFaithfulness,
    double MinimumPerQueryFaithfulness)
{
    /// <summary>Total context chunks passed across all evaluated queries.</summary>
    public int TotalContextChunkCount => QueryResults.Sum(r => r.ContextChunkIds.Count);

    /// <summary>Total generated answer content length (characters) across all evaluated queries.</summary>
    public int TotalAnswerContentLength => QueryResults.Sum(r => r.AnswerContentLength);
}

/// <summary>
/// Drives a real <see cref="INetIndexPipeline"/> through ingest and generation for each faithfulness
/// query in a dataset, scoring the generated answer against the context chunks actually passed to the
/// <see cref="DeterministicFaithfulnessChatClient"/>.
/// </summary>
internal sealed class FaithfulnessEvaluationRunner(
    INetIndexPipeline pipeline,
    DeterministicFaithfulnessChatClient chatClient)
{
    /// <summary>
    /// Ingests all corpus documents into the pipeline.
    /// </summary>
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

    /// <summary>
    /// Evaluates faithfulness for every query in <paramref name="dataset"/> that carries a
    /// <see cref="FaithfulnessExpectation"/>.
    /// </summary>
    public async Task<FaithfulnessEvaluationReport> EvaluateAsync(
        RetrievalEvaluationDataset dataset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        var faithfulnessQueries = dataset.Queries
            .Where(q => q.Faithfulness is not null)
            .ToList();

        if (faithfulnessQueries.Count == 0)
        {
            throw new InvalidOperationException("Cannot evaluate faithfulness over a dataset with zero faithfulness queries.");
        }

        var queryResults = new List<FaithfulnessQueryResult>(faithfulnessQueries.Count);

        foreach (var query in faithfulnessQueries)
        {
            var expectation = query.Faithfulness
                ?? throw new InvalidOperationException($"Query '{query.Id}' is missing a faithfulness expectation.");
            var expectedContextChunkIds = expectation.ExpectedContextChunkIds.ToList().AsReadOnly();
            var answerBuilder = new System.Text.StringBuilder();

            chatClient.ResetCapture(query.Text);
            await foreach (var chunk in pipeline.GenerateAsync(query.Text, cancellationToken)
                .WithCancellation(cancellationToken))
            {
                // AC-4: terminal empty chunks are ignored for answer content, but terminal chunks
                // carrying text are still part of the generated answer.
                if (!string.IsNullOrEmpty(chunk.Text))
                {
                    answerBuilder.Append(chunk.Text);
                }
            }

            // Context was captured by the DeterministicFaithfulnessChatClient during the GenerateAsync call.
            // Do not re-query the pipeline or reflect into NetIndexPipeline to infer context.
            var capturedChunks = chatClient.GetCapturedChunks(query.Text);
            var contextChunkIds = capturedChunks.Select(c => c.Id).ToList().AsReadOnly();
            var missingExpectedChunkIds = expectedContextChunkIds
                .Except(contextChunkIds, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();

            var faithfulness = FaithfulnessMetrics.Compute(answerBuilder.ToString(), capturedChunks);

            queryResults.Add(new FaithfulnessQueryResult(
                QueryId: query.Id,
                AnswerContentLength: answerBuilder.Length,
                ExpectedContextChunkIds: expectedContextChunkIds,
                ContextChunkIds: contextChunkIds,
                MissingExpectedContextChunkIds: missingExpectedChunkIds,
                FaithfulnessScore: faithfulness.Score,
                UnsupportedTokens: faithfulness.UnsupportedTokens));
        }

        var meanFaithfulness = FaithfulnessMetrics.MeanFaithfulness(
            queryResults.Select(r => r.FaithfulnessScore).ToList());

        var minimumFaithfulness = queryResults.Min(r => r.FaithfulnessScore);

        return new FaithfulnessEvaluationReport(queryResults, meanFaithfulness, minimumFaithfulness);
    }
}

/// <summary>
/// Enforces a faithfulness evaluation report against committed thresholds, failing with expected and
/// actual values rather than a bare boolean.
/// </summary>
internal static class FaithfulnessThresholdGate
{
    public static void AssertMeetsThresholds(
        FaithfulnessEvaluationReport report,
        FaithfulnessThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(thresholds);

        if (report.QueryResults.Count == 0)
        {
            throw new InvalidOperationException(
                "Faithfulness quality gate cannot pass when zero faithfulness queries were evaluated.");
        }

        if (report.TotalContextChunkCount == 0)
        {
            throw new InvalidOperationException(
                "Faithfulness quality gate cannot pass when zero context chunks were passed to the chat client.");
        }

        if (report.TotalAnswerContentLength == 0)
        {
            throw new InvalidOperationException(
                "Faithfulness quality gate cannot pass when zero generated answer content was produced.");
        }

        if (!double.IsFinite(report.MeanFaithfulness) || report.MeanFaithfulness < thresholds.MeanFaithfulness)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"Mean faithfulness {report.MeanFaithfulness:F4} is below threshold {thresholds.MeanFaithfulness:F4}."));
        }

        if (!double.IsFinite(report.MinimumPerQueryFaithfulness)
            || report.MinimumPerQueryFaithfulness < thresholds.MinimumPerQueryFaithfulness)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"Minimum per-query faithfulness {report.MinimumPerQueryFaithfulness:F4} is below threshold {thresholds.MinimumPerQueryFaithfulness:F4}."));
        }
    }
}
