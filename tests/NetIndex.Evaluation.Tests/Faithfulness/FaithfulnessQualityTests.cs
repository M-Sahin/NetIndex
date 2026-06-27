using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using NetIndex.Evaluation.Tests.Fixtures;
using NetIndex.Evaluation.Tests.TestSupport;
using Xunit.Abstractions;

namespace NetIndex.Evaluation.Tests.Faithfulness;

public class FaithfulnessQualityTests(ITestOutputHelper output)
{
    private static readonly string DatasetPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "retrieval-evaluation-dataset.json");

    private static readonly IReadOnlyDictionary<string, string> NoMetadata =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private const string TestTenantId = "evaluation-tenant";

    // ── AC-5/AC-6: real, offline, deterministic end-to-end quality gates ──

    [Fact]
    [Trait("Category", "Evaluation")]
    public async Task CommittedDataset_FaithfulnessQuality_MeetsThresholdsAsync()
    {
        var dataset = RetrievalEvaluationDatasetLoader.LoadFromFile(DatasetPath);
        Assert.NotNull(dataset.FaithfulnessThresholds);

        var (pipeline, chatClient) = BuildPipelineWithChatClient();
        var runner = new FaithfulnessEvaluationRunner(pipeline, chatClient);

        await runner.IngestAsync(dataset.Documents);
        var report = await runner.EvaluateAsync(dataset);

        LogReport(report, dataset.FaithfulnessThresholds);

        FaithfulnessThresholdGate.AssertMeetsThresholds(report, dataset.FaithfulnessThresholds);
    }

    [Fact]
    [Trait("Category", "Evaluation")]
    public async Task CommittedDataset_RepeatedFaithfulnessEvaluation_IsStableAsync()
    {
        var dataset = RetrievalEvaluationDatasetLoader.LoadFromFile(DatasetPath);
        Assert.NotNull(dataset.FaithfulnessThresholds);

        var (firstPipeline, firstChatClient) = BuildPipelineWithChatClient();
        var firstRunner = new FaithfulnessEvaluationRunner(firstPipeline, firstChatClient);
        await firstRunner.IngestAsync(dataset.Documents);
        var firstReport = await firstRunner.EvaluateAsync(dataset);

        var (secondPipeline, secondChatClient) = BuildPipelineWithChatClient();
        var secondRunner = new FaithfulnessEvaluationRunner(secondPipeline, secondChatClient);
        await secondRunner.IngestAsync(dataset.Documents);
        var secondReport = await secondRunner.EvaluateAsync(dataset);

        Assert.Equal(firstReport.MeanFaithfulness, secondReport.MeanFaithfulness, precision: 10);
        Assert.Equal(firstReport.MinimumPerQueryFaithfulness, secondReport.MinimumPerQueryFaithfulness, precision: 10);
        Assert.Equal(firstReport.QueryResults.Count, secondReport.QueryResults.Count);

        for (var i = 0; i < firstReport.QueryResults.Count; i++)
        {
            Assert.Equal(firstReport.QueryResults[i].QueryId, secondReport.QueryResults[i].QueryId);
            Assert.Equal(firstReport.QueryResults[i].ExpectedContextChunkIds, secondReport.QueryResults[i].ExpectedContextChunkIds);
            Assert.Equal(firstReport.QueryResults[i].ContextChunkIds, secondReport.QueryResults[i].ContextChunkIds);
            Assert.Equal(firstReport.QueryResults[i].MissingExpectedContextChunkIds, secondReport.QueryResults[i].MissingExpectedContextChunkIds);
            Assert.Equal(firstReport.QueryResults[i].FaithfulnessScore, secondReport.QueryResults[i].FaithfulnessScore, precision: 10);
        }
    }

    // ── Runner unit tests (untagged — run in PR lane) ──

    [Fact]
    public async Task Runner_ConsumesAllGenerationChunks_IgnoresEmptyTerminalChunkAsync()
    {
        var dataset = RetrievalEvaluationDatasetLoader.LoadFromFile(DatasetPath);
        var (pipeline, chatClient) = BuildPipelineWithChatClient();
        var runner = new FaithfulnessEvaluationRunner(pipeline, chatClient);

        await runner.IngestAsync(dataset.Documents);
        var report = await runner.EvaluateAsync(dataset);

        Assert.All(report.QueryResults, result =>
            Assert.True(result.AnswerContentLength > 0,
                $"Query '{result.QueryId}' produced an empty answer — the terminal marker may have been mishandled."));
    }

    [Fact]
    public async Task Runner_IncludesTerminalChunkText_WhenTerminalCarriesTextAsync()
    {
        var chatClient = new DeterministicFaithfulnessChatClient();
        var runner = new FaithfulnessEvaluationRunner(new TerminalTextGeneratePipeline(chatClient), chatClient);
        var dataset = new RetrievalEvaluationDataset(
            TopK: 1,
            Thresholds: new RetrievalEvaluationThresholds(MeanReciprocalRank: 0.0, MeanNdcgAtK: 0.0),
            Documents: [new RetrievalEvaluationDocument("doc-terminal", "alpha omega")],
            Queries:
            [
                new RetrievalEvaluationQuery(
                    "q-terminal",
                    "alpha",
                    [new RetrievalRelevanceJudgment("c1", 3)])
                {
                    Faithfulness = new FaithfulnessExpectation(["c1"]),
                },
            ])
        {
            FaithfulnessThresholds = new FaithfulnessThresholds(MeanFaithfulness: 1.0, MinimumPerQueryFaithfulness: 1.0),
        };

        var report = await runner.EvaluateAsync(dataset);

        var result = Assert.Single(report.QueryResults);
        Assert.Equal("alpha omega".Length, result.AnswerContentLength);
        Assert.Equal(["c1"], result.ExpectedContextChunkIds);
        Assert.Equal(["c1"], result.ContextChunkIds);
        Assert.Empty(result.MissingExpectedContextChunkIds);
        Assert.Equal(1.0, result.FaithfulnessScore, precision: 10);
    }

    [Fact]
    public async Task EvaluateAsync_PropagatesCancellationToGenerateMethodAndEnumeratorAsync()
    {
        var dataset = RetrievalEvaluationDatasetLoader.LoadFromFile(DatasetPath);
        var chatClient = new DeterministicFaithfulnessChatClient();
        var fakePipeline = new CancellationCapturingGeneratePipeline(chatClient);
        var runner = new FaithfulnessEvaluationRunner(fakePipeline, chatClient);

        using var cts = new CancellationTokenSource();

        var singleQueryDataset = dataset with
        {
            Queries = [dataset.Queries.First(q => q.Faithfulness is not null)],
        };

        await runner.EvaluateAsync(singleQueryDataset, cts.Token);

        Assert.Equal(cts.Token, fakePipeline.CapturedGenerateToken);
        Assert.Equal(cts.Token, fakePipeline.CapturedEnumeratorToken);
    }

    // ── Threshold gate unit tests (untagged) ──

    [Fact]
    public void AssertMeetsThresholds_BelowMeanFaithfulnessThreshold_ThrowsWithExpectedAndActual()
    {
        var report = QueryResultReport(FaithfulnessScore: 0.7, MeanFaithfulness: 0.7, MinimumPerQueryFaithfulness: 0.7);
        var thresholds = new FaithfulnessThresholds(MeanFaithfulness: 0.85, MinimumPerQueryFaithfulness: 0.65);

        var exception = Assert.Throws<InvalidOperationException>(
            () => FaithfulnessThresholdGate.AssertMeetsThresholds(report, thresholds));

        Assert.Contains("0.7000", exception.Message, StringComparison.Ordinal);
        Assert.Contains("0.8500", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssertMeetsThresholds_BelowMinimumPerQueryFaithfulnessThreshold_ThrowsWithExpectedAndActual()
    {
        var report = QueryResultReport(FaithfulnessScore: 0.5, MeanFaithfulness: 0.9, MinimumPerQueryFaithfulness: 0.5);
        var thresholds = new FaithfulnessThresholds(MeanFaithfulness: 0.85, MinimumPerQueryFaithfulness: 0.65);

        var exception = Assert.Throws<InvalidOperationException>(
            () => FaithfulnessThresholdGate.AssertMeetsThresholds(report, thresholds));

        Assert.Contains("0.5000", exception.Message, StringComparison.Ordinal);
        Assert.Contains("0.6500", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssertMeetsThresholds_ZeroEvaluatedQueries_Throws()
    {
        var report = new FaithfulnessEvaluationReport([], MeanFaithfulness: 0.0, MinimumPerQueryFaithfulness: 0.0);
        var thresholds = new FaithfulnessThresholds(MeanFaithfulness: 0.85, MinimumPerQueryFaithfulness: 0.65);

        Assert.Throws<InvalidOperationException>(
            () => FaithfulnessThresholdGate.AssertMeetsThresholds(report, thresholds));
    }

    [Fact]
    public void AssertMeetsThresholds_ZeroContextChunksPassed_Throws()
    {
        var report = new FaithfulnessEvaluationReport(
            [new FaithfulnessQueryResult(
                QueryId: "q1",
                AnswerContentLength: 5,
                ExpectedContextChunkIds: ["c1"],
                ContextChunkIds: [],
                MissingExpectedContextChunkIds: ["c1"],
                FaithfulnessScore: 0.0,
                UnsupportedTokens: [])],
            MeanFaithfulness: 0.0,
            MinimumPerQueryFaithfulness: 0.0);
        var thresholds = new FaithfulnessThresholds(MeanFaithfulness: 0.85, MinimumPerQueryFaithfulness: 0.65);

        Assert.Throws<InvalidOperationException>(
            () => FaithfulnessThresholdGate.AssertMeetsThresholds(report, thresholds));
    }

    [Fact]
    public void AssertMeetsThresholds_ZeroAnswerContentLength_Throws()
    {
        var report = new FaithfulnessEvaluationReport(
            [new FaithfulnessQueryResult(
                QueryId: "q1",
                AnswerContentLength: 0,
                ExpectedContextChunkIds: ["c1"],
                ContextChunkIds: ["c1"],
                MissingExpectedContextChunkIds: [],
                FaithfulnessScore: 0.0,
                UnsupportedTokens: [])],
            MeanFaithfulness: 0.0,
            MinimumPerQueryFaithfulness: 0.0);
        var thresholds = new FaithfulnessThresholds(MeanFaithfulness: 0.85, MinimumPerQueryFaithfulness: 0.65);

        Assert.Throws<InvalidOperationException>(
            () => FaithfulnessThresholdGate.AssertMeetsThresholds(report, thresholds));
    }

    // ── Helpers ──

    private static (INetIndexPipeline Pipeline, DeterministicFaithfulnessChatClient ChatClient) BuildPipelineWithChatClient()
    {
        var chatClient = new DeterministicFaithfulnessChatClient();
        var services = new ServiceCollection();
        services.AddSingleton<ITenantResolver>(new FixedTenantResolver(TestTenantId));
        services.AddSingleton<IEmbeddingGenerator>(new DeterministicTokenEmbeddingGenerator(384));
        services.AddSingleton<IChatClient>(chatClient);
        services.AddNetIndex().Build();

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<INetIndexPipeline>(), chatClient);
    }

    private static RagChunk Chunk(string id, string text) =>
        new(id, text, Embedding: null, DocumentId: id, Metadata: NoMetadata);

    private static FaithfulnessEvaluationReport QueryResultReport(
        double FaithfulnessScore,
        double MeanFaithfulness,
        double MinimumPerQueryFaithfulness) =>
        new(
            [new FaithfulnessQueryResult(
                QueryId: "q1",
                AnswerContentLength: 10,
                ExpectedContextChunkIds: ["c1"],
                ContextChunkIds: ["c1"],
                MissingExpectedContextChunkIds: [],
                FaithfulnessScore: FaithfulnessScore,
                UnsupportedTokens: [])],
            MeanFaithfulness,
            MinimumPerQueryFaithfulness);

    private void LogReport(FaithfulnessEvaluationReport report, FaithfulnessThresholds thresholds)
    {
        var passState = IsPassing(report, thresholds) ? "pass" : "fail";
        foreach (var result in report.QueryResults)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"query={result.QueryId} faithfulness={result.FaithfulnessScore:F4} " +
                $"answerLen={result.AnswerContentLength} expected=[{string.Join(", ", result.ExpectedContextChunkIds)}] " +
                $"context=[{string.Join(", ", result.ContextChunkIds)}] " +
                $"missingExpected=[{string.Join(", ", result.MissingExpectedContextChunkIds)}] " +
                $"unsupported=[{string.Join(", ", result.UnsupportedTokens)}]"));
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"passState={passState}; meanFaithfulness={report.MeanFaithfulness:F4} " +
            $"(threshold {thresholds.MeanFaithfulness:F4}); " +
            $"minFaithfulness={report.MinimumPerQueryFaithfulness:F4} " +
            $"(threshold {thresholds.MinimumPerQueryFaithfulness:F4})"));
    }

    private static bool IsPassing(FaithfulnessEvaluationReport report, FaithfulnessThresholds thresholds) =>
        report.QueryResults.Count > 0
        && report.TotalContextChunkCount > 0
        && report.TotalAnswerContentLength > 0
        && double.IsFinite(report.MeanFaithfulness)
        && report.MeanFaithfulness >= thresholds.MeanFaithfulness
        && double.IsFinite(report.MinimumPerQueryFaithfulness)
        && report.MinimumPerQueryFaithfulness >= thresholds.MinimumPerQueryFaithfulness;

    /// <summary>
    /// Thin async-enumerable test double: captures both the token passed to
    /// <see cref="INetIndexPipeline.GenerateAsync"/> and the token the runner supplies to
    /// <c>GetAsyncEnumerator</c> via <c>.WithCancellation(...)</c>.
    /// </summary>
    private sealed class CancellationCapturingGeneratePipeline(DeterministicFaithfulnessChatClient chatClient) : INetIndexPipeline
    {
        private CapturingAsyncEnumerable? _generateResults;

        public CancellationToken? CapturedGenerateToken { get; private set; }

        public CancellationToken? CapturedEnumeratorToken => _generateResults?.CapturedToken;

        public Task IngestAsync(IDocument document, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public IAsyncEnumerable<SearchResult<RagChunk>> QueryAsync(string query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by the faithfulness evaluation runner.");

        public IAsyncEnumerable<GenerationChunk> GenerateAsync(string query, CancellationToken cancellationToken = default)
        {
            CapturedGenerateToken = cancellationToken;
            _generateResults = new CapturingAsyncEnumerable(
                chatClient.GenerateStreamingAsync(query, [Chunk("c1", "context")], cancellationToken));
            return _generateResults;
        }

        private sealed class CapturingAsyncEnumerable(IAsyncEnumerable<GenerationChunk> inner) : IAsyncEnumerable<GenerationChunk>
        {
            public CancellationToken? CapturedToken { get; private set; }

            public IAsyncEnumerator<GenerationChunk> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                CapturedToken = cancellationToken;
                return Enumerate(cancellationToken);
            }

            private async IAsyncEnumerator<GenerationChunk> Enumerate(
                CancellationToken cancellationToken = default)
            {
                await foreach (var chunk in inner.WithCancellation(cancellationToken))
                {
                    yield return chunk;
                }
            }
        }
    }

    private sealed class TerminalTextGeneratePipeline(DeterministicFaithfulnessChatClient chatClient) : INetIndexPipeline
    {
        public Task IngestAsync(IDocument document, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public IAsyncEnumerable<SearchResult<RagChunk>> QueryAsync(string query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by the faithfulness evaluation runner.");

        public async IAsyncEnumerable<GenerationChunk> GenerateAsync(
            string query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await chatClient.GenerateAsync(query, [Chunk("c1", "alpha omega")], cancellationToken)
                .ConfigureAwait(false);
            yield return new GenerationChunk("alpha ", IsComplete: false, FinishReason: FinishReason.Stop);
            cancellationToken.ThrowIfCancellationRequested();
            yield return new GenerationChunk("omega", IsComplete: true, FinishReason: FinishReason.Stop);
        }
    }
}
