using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using NetIndex.Evaluation.Tests.Fixtures;
using NetIndex.Evaluation.Tests.TestSupport;
using Xunit.Abstractions;

namespace NetIndex.Evaluation.Tests.Retrieval;

public class RetrievalQualityTests(ITestOutputHelper output)
{
    private static readonly string DatasetPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "retrieval-evaluation-dataset.json");

    private const string TestTenantId = "evaluation-tenant";

    // ── AC-5/AC-6: real, offline, deterministic end-to-end quality gate ──

    [Fact]
    [Trait("Category", "Evaluation")]
    public async Task CommittedDataset_RetrievalQuality_MeetsThresholdsAsync()
    {
        var dataset = RetrievalEvaluationDatasetLoader.LoadFromFile(DatasetPath);
        var runner = new RetrievalEvaluationRunner(BuildPipeline());

        await runner.IngestAsync(dataset.Documents);
        var report = await runner.EvaluateAsync(dataset);

        LogReport(report, dataset.Thresholds);

        RetrievalThresholdGate.AssertMeetsThresholds(report, dataset.Thresholds);
    }

    [Fact]
    [Trait("Category", "Evaluation")]
    public async Task CommittedDataset_RepeatedEvaluation_IsStableAsync()
    {
        var dataset = RetrievalEvaluationDatasetLoader.LoadFromFile(DatasetPath);

        var firstRunner = new RetrievalEvaluationRunner(BuildPipeline());
        await firstRunner.IngestAsync(dataset.Documents);
        var firstReport = await firstRunner.EvaluateAsync(dataset);

        var secondRunner = new RetrievalEvaluationRunner(BuildPipeline());
        await secondRunner.IngestAsync(dataset.Documents);
        var secondReport = await secondRunner.EvaluateAsync(dataset);

        Assert.Equal(firstReport.MeanReciprocalRank, secondReport.MeanReciprocalRank, precision: 10);
        Assert.Equal(firstReport.MeanNdcgAtK, secondReport.MeanNdcgAtK, precision: 10);

        Assert.Equal(firstReport.QueryResults.Count, secondReport.QueryResults.Count);
        for (var i = 0; i < firstReport.QueryResults.Count; i++)
        {
            Assert.Equal(firstReport.QueryResults[i].QueryId, secondReport.QueryResults[i].QueryId);
            Assert.Equal(firstReport.QueryResults[i].RankedChunkIds, secondReport.QueryResults[i].RankedChunkIds);
        }
    }

    // ── Task 6: cancellation propagation, proven with a thin test double (not a real pipeline) ──

    [Fact]
    public async Task EvaluateAsync_PropagatesCancellationToTheQueryEnumeratorAsync()
    {
        var fakePipeline = new CancellationCapturingPipeline();
        var runner = new RetrievalEvaluationRunner(fakePipeline);
        var dataset = RetrievalEvaluationDatasetLoader.LoadFromFile(DatasetPath);
        var singleQueryDataset = dataset with { Queries = [dataset.Queries[0]] };

        using var cts = new CancellationTokenSource();

        await runner.EvaluateAsync(singleQueryDataset, cts.Token);

        Assert.Equal(cts.Token, fakePipeline.CapturedEnumeratorToken);
    }

    // ── Task 6: threshold gate rejects a synthetic below-threshold report with expected/actual ──

    [Fact]
    public void AssertMeetsThresholds_BelowMrrThreshold_ThrowsWithExpectedAndActual()
    {
        var report = new RetrievalEvaluationReport(
            [new RetrievalQueryResult("q1", ["doc-a_chunk_0"], [0.9f], ReciprocalRank: 0.5, NdcgAtK: 0.9)],
            MeanReciprocalRank: 0.5,
            MeanNdcgAtK: 0.9);
        var thresholds = new RetrievalEvaluationThresholds(MeanReciprocalRank: 0.8, MeanNdcgAtK: 0.75);

        var exception = Assert.Throws<InvalidOperationException>(
            () => RetrievalThresholdGate.AssertMeetsThresholds(report, thresholds));

        Assert.Contains("0.5000", exception.Message, StringComparison.Ordinal);
        Assert.Contains("0.8000", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssertMeetsThresholds_BelowNdcgThreshold_ThrowsWithExpectedAndActual()
    {
        var report = new RetrievalEvaluationReport(
            [new RetrievalQueryResult("q1", ["doc-a_chunk_0"], [0.9f], ReciprocalRank: 1.0, NdcgAtK: 0.4)],
            MeanReciprocalRank: 1.0,
            MeanNdcgAtK: 0.4);
        var thresholds = new RetrievalEvaluationThresholds(MeanReciprocalRank: 0.8, MeanNdcgAtK: 0.75);

        var exception = Assert.Throws<InvalidOperationException>(
            () => RetrievalThresholdGate.AssertMeetsThresholds(report, thresholds));

        Assert.Contains("0.4000", exception.Message, StringComparison.Ordinal);
        Assert.Contains("0.7500", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssertMeetsThresholds_ZeroEvaluatedQueries_Throws()
    {
        var report = new RetrievalEvaluationReport([], MeanReciprocalRank: 0.0, MeanNdcgAtK: 0.0);
        var thresholds = new RetrievalEvaluationThresholds(MeanReciprocalRank: 0.8, MeanNdcgAtK: 0.75);

        Assert.Throws<InvalidOperationException>(
            () => RetrievalThresholdGate.AssertMeetsThresholds(report, thresholds));
    }

    // ── Helpers ──

    private static INetIndexPipeline BuildPipeline()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantResolver>(new FixedTenantResolver(TestTenantId));
        services.AddSingleton<IEmbeddingGenerator>(new DeterministicTokenEmbeddingGenerator(384));
        services.AddNetIndex().Build();

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<INetIndexPipeline>();
    }

    private void LogReport(RetrievalEvaluationReport report, RetrievalEvaluationThresholds thresholds)
    {
        foreach (var result in report.QueryResults)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"query={result.QueryId} rr={result.ReciprocalRank:F4} ndcg={result.NdcgAtK:F4} " +
                $"ranked=[{string.Join(", ", result.RankedChunkIds.Zip(result.Scores, (id, score) => $"{id}:{score.ToString("F4", CultureInfo.InvariantCulture)}"))}]"));
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"MRR={report.MeanReciprocalRank:F4} (threshold {thresholds.MeanReciprocalRank:F4}); " +
            $"meanNdcg={report.MeanNdcgAtK:F4} (threshold {thresholds.MeanNdcgAtK:F4})"));
    }

    /// <summary>
    /// Thin async-enumerable test double: captures the <see cref="CancellationToken"/> the runner
    /// supplies to <c>GetAsyncEnumerator</c> (i.e. via <c>.WithCancellation(...)</c>), which a
    /// pre-cancelled call to a real pipeline cannot distinguish from cancellation before enumeration.
    /// </summary>
    private sealed class CancellationCapturingPipeline : INetIndexPipeline
    {
        private readonly CapturingAsyncEnumerable _results = new();

        public CancellationToken? CapturedEnumeratorToken => _results.CapturedToken;

        public Task IngestAsync(IDocument document, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public IAsyncEnumerable<SearchResult<RagChunk>> QueryAsync(string query, CancellationToken cancellationToken = default)
            => _results;

        public IAsyncEnumerable<GenerationChunk> GenerateAsync(string query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not exercised by the retrieval evaluation runner.");

        private sealed class CapturingAsyncEnumerable : IAsyncEnumerable<SearchResult<RagChunk>>
        {
            public CancellationToken? CapturedToken { get; private set; }

            public IAsyncEnumerator<SearchResult<RagChunk>> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                CapturedToken = cancellationToken;
                return Empty();
            }

            private static async IAsyncEnumerator<SearchResult<RagChunk>> Empty()
            {
                await Task.CompletedTask;
                yield break;
            }
        }
    }
}
