using System.Globalization;

namespace NetIndex.Evaluation.Tests.Fixtures;

public class RetrievalEvaluationDatasetLoaderTests
{
    private const string ValidJson = """
        {
          "topK": 2,
          "thresholds": { "meanReciprocalRank": 0.5, "meanNdcgAtK": 0.5 },
          "documents": [
            { "id": "doc-a", "content": "alpha" },
            { "id": "doc-b", "content": "beta" }
          ],
          "queries": [
            {
              "id": "q1",
              "text": "alpha query",
              "relevance": [ { "chunkId": "doc-a_chunk_0", "grade": 3 } ]
            }
          ]
        }
        """;

    [Fact]
    public void LoadFromJson_ValidDataset_ParsesSuccessfully()
    {
        var dataset = RetrievalEvaluationDatasetLoader.LoadFromJson(ValidJson);

        Assert.Equal(2, dataset.TopK);
        Assert.Equal(2, dataset.Documents.Count);
        Assert.Single(dataset.Queries);
    }

    [Fact]
    public void LoadFromJson_EmptyDocuments_Throws()
    {
        const string json = """
            {
              "topK": 1,
              "thresholds": { "meanReciprocalRank": 0.5, "meanNdcgAtK": 0.5 },
              "documents": [],
              "queries": [
                { "id": "q1", "text": "alpha", "relevance": [ { "chunkId": "doc-a_chunk_0", "grade": 1 } ] }
              ]
            }
            """;
        Assert.Throws<InvalidDataException>(() => RetrievalEvaluationDatasetLoader.LoadFromJson(json));
    }

    [Fact]
    public void LoadFromJson_NoQueries_Throws()
    {
        var json = """
            {
              "topK": 1,
              "thresholds": { "meanReciprocalRank": 0.5, "meanNdcgAtK": 0.5 },
              "documents": [ { "id": "doc-a", "content": "alpha" } ],
              "queries": []
            }
            """;
        Assert.Throws<InvalidDataException>(() => RetrievalEvaluationDatasetLoader.LoadFromJson(json));
    }

    [Fact]
    public void LoadFromJson_DuplicateDocumentIds_Throws()
    {
        var json = """
            {
              "topK": 1,
              "thresholds": { "meanReciprocalRank": 0.5, "meanNdcgAtK": 0.5 },
              "documents": [
                { "id": "doc-a", "content": "alpha" },
                { "id": "doc-a", "content": "alpha again" }
              ],
              "queries": [
                { "id": "q1", "text": "alpha", "relevance": [ { "chunkId": "doc-a_chunk_0", "grade": 1 } ] }
              ]
            }
            """;
        Assert.Throws<InvalidDataException>(() => RetrievalEvaluationDatasetLoader.LoadFromJson(json));
    }

    [Fact]
    public void LoadFromJson_DuplicateQueryIds_Throws()
    {
        var json = """
            {
              "topK": 1,
              "thresholds": { "meanReciprocalRank": 0.5, "meanNdcgAtK": 0.5 },
              "documents": [ { "id": "doc-a", "content": "alpha" } ],
              "queries": [
                { "id": "q1", "text": "alpha", "relevance": [ { "chunkId": "doc-a_chunk_0", "grade": 1 } ] },
                { "id": "q1", "text": "alpha again", "relevance": [ { "chunkId": "doc-a_chunk_0", "grade": 1 } ] }
              ]
            }
            """;
        Assert.Throws<InvalidDataException>(() => RetrievalEvaluationDatasetLoader.LoadFromJson(json));
    }

    [Fact]
    public void LoadFromJson_UnknownJudgedChunk_Throws()
    {
        var json = """
            {
              "topK": 1,
              "thresholds": { "meanReciprocalRank": 0.5, "meanNdcgAtK": 0.5 },
              "documents": [ { "id": "doc-a", "content": "alpha" } ],
              "queries": [
                { "id": "q1", "text": "alpha", "relevance": [ { "chunkId": "doc-unknown_chunk_0", "grade": 1 } ] }
              ]
            }
            """;
        Assert.Throws<InvalidDataException>(() => RetrievalEvaluationDatasetLoader.LoadFromJson(json));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void LoadFromJson_OutOfRangeGrade_Throws(int grade)
    {
        var json = $$"""
            {
              "topK": 1,
              "thresholds": { "meanReciprocalRank": 0.5, "meanNdcgAtK": 0.5 },
              "documents": [ { "id": "doc-a", "content": "alpha" } ],
              "queries": [
                { "id": "q1", "text": "alpha", "relevance": [ { "chunkId": "doc-a_chunk_0", "grade": {{grade}} } ] }
              ]
            }
            """;
        Assert.Throws<InvalidDataException>(() => RetrievalEvaluationDatasetLoader.LoadFromJson(json));
    }

    [Fact]
    public void LoadFromJson_NoPositiveJudgment_Throws()
    {
        var json = """
            {
              "topK": 1,
              "thresholds": { "meanReciprocalRank": 0.5, "meanNdcgAtK": 0.5 },
              "documents": [ { "id": "doc-a", "content": "alpha" } ],
              "queries": [
                { "id": "q1", "text": "alpha", "relevance": [ { "chunkId": "doc-a_chunk_0", "grade": 0 } ] }
              ]
            }
            """;
        Assert.Throws<InvalidDataException>(() => RetrievalEvaluationDatasetLoader.LoadFromJson(json));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void LoadFromJson_InvalidTopK_Throws(int topK)
    {
        var json = $$"""
            {
              "topK": {{topK}},
              "thresholds": { "meanReciprocalRank": 0.5, "meanNdcgAtK": 0.5 },
              "documents": [ { "id": "doc-a", "content": "alpha" } ],
              "queries": [
                { "id": "q1", "text": "alpha", "relevance": [ { "chunkId": "doc-a_chunk_0", "grade": 1 } ] }
              ]
            }
            """;
        Assert.Throws<InvalidDataException>(() => RetrievalEvaluationDatasetLoader.LoadFromJson(json));
    }

    [Theory]
    [InlineData(-0.1, 0.5)]
    [InlineData(1.1, 0.5)]
    [InlineData(0.5, -0.1)]
    [InlineData(0.5, 1.1)]
    public void LoadFromJson_InvalidThresholds_Throws(double mrr, double ndcg)
    {
        var mrrLiteral = mrr.ToString(CultureInfo.InvariantCulture);
        var ndcgLiteral = ndcg.ToString(CultureInfo.InvariantCulture);
        var json = $$"""
            {
              "topK": 1,
              "thresholds": { "meanReciprocalRank": {{mrrLiteral}}, "meanNdcgAtK": {{ndcgLiteral}} },
              "documents": [ { "id": "doc-a", "content": "alpha" } ],
              "queries": [
                { "id": "q1", "text": "alpha", "relevance": [ { "chunkId": "doc-a_chunk_0", "grade": 1 } ] }
              ]
            }
            """;
        Assert.Throws<InvalidDataException>(() => RetrievalEvaluationDatasetLoader.LoadFromJson(json));
    }

    [Fact]
    public void BuildJudgmentLookup_DuplicateChunkIdInSameQuery_Throws()
    {
        var query = new RetrievalEvaluationQuery(
            "q1",
            "alpha",
            [
                new RetrievalRelevanceJudgment("doc-a_chunk_0", 1),
                new RetrievalRelevanceJudgment("doc-a_chunk_0", 2),
            ]);

        Assert.Throws<InvalidDataException>(() => RetrievalEvaluationDatasetLoader.BuildJudgmentLookup(query));
    }

    // ── Faithfulness validation ──

    [Fact]
    public void LoadFromJson_ValidFaithfulnessThresholds_ParsesSuccessfully()
    {
        const string json = """
            {
              "topK": 1,
              "thresholds": { "meanReciprocalRank": 0.5, "meanNdcgAtK": 0.5 },
              "faithfulnessThresholds": { "meanFaithfulness": 0.8, "minimumPerQueryFaithfulness": 0.6 },
              "documents": [ { "id": "doc-a", "content": "alpha" } ],
              "queries": [
                {
                  "id": "q1",
                  "text": "alpha query",
                  "relevance": [ { "chunkId": "doc-a_chunk_0", "grade": 1 } ],
                  "faithfulness": { "expectedContextChunkIds": ["doc-a_chunk_0"] }
                }
              ]
            }
            """;

        var dataset = RetrievalEvaluationDatasetLoader.LoadFromJson(json);

        Assert.NotNull(dataset.FaithfulnessThresholds);
        Assert.Equal(0.8, dataset.FaithfulnessThresholds.MeanFaithfulness);
        Assert.Equal(0.6, dataset.FaithfulnessThresholds.MinimumPerQueryFaithfulness);
        var faithfulness = Assert.Single(dataset.Queries).Faithfulness;
        Assert.NotNull(faithfulness);
        Assert.Equal("doc-a_chunk_0", Assert.Single(faithfulness.ExpectedContextChunkIds));
    }

    [Theory]
    [InlineData(-0.1, 0.5)]
    [InlineData(1.1, 0.5)]
    [InlineData(0.5, -0.1)]
    [InlineData(0.5, 1.1)]
    public void LoadFromJson_InvalidFaithfulnessThresholds_Throws(double mean, double min)
    {
        var meanLiteral = mean.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var minLiteral = min.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var json = $$"""
            {
              "topK": 1,
              "thresholds": { "meanReciprocalRank": 0.5, "meanNdcgAtK": 0.5 },
              "faithfulnessThresholds": { "meanFaithfulness": {{meanLiteral}}, "minimumPerQueryFaithfulness": {{minLiteral}} },
              "documents": [ { "id": "doc-a", "content": "alpha" } ],
              "queries": [
                {
                  "id": "q1",
                  "text": "alpha",
                  "relevance": [ { "chunkId": "doc-a_chunk_0", "grade": 1 } ],
                  "faithfulness": { "expectedContextChunkIds": ["doc-a_chunk_0"] }
                }
              ]
            }
            """;
        Assert.Throws<InvalidDataException>(() => RetrievalEvaluationDatasetLoader.LoadFromJson(json));
    }

    [Fact]
    public void LoadFromJson_FaithfulnessThresholdsPresent_QueryMissingFaithfulness_Throws()
    {
        const string json = """
            {
              "topK": 1,
              "thresholds": { "meanReciprocalRank": 0.5, "meanNdcgAtK": 0.5 },
              "faithfulnessThresholds": { "meanFaithfulness": 0.8, "minimumPerQueryFaithfulness": 0.6 },
              "documents": [ { "id": "doc-a", "content": "alpha" } ],
              "queries": [
                { "id": "q1", "text": "alpha", "relevance": [ { "chunkId": "doc-a_chunk_0", "grade": 1 } ] }
              ]
            }
            """;
        Assert.Throws<InvalidDataException>(() => RetrievalEvaluationDatasetLoader.LoadFromJson(json));
    }

    [Fact]
    public void LoadFromJson_FaithfulnessExpectationWithoutThresholds_Throws()
    {
        const string json = """
            {
              "topK": 1,
              "thresholds": { "meanReciprocalRank": 0.5, "meanNdcgAtK": 0.5 },
              "documents": [ { "id": "doc-a", "content": "alpha" } ],
              "queries": [
                {
                  "id": "q1",
                  "text": "alpha",
                  "relevance": [ { "chunkId": "doc-a_chunk_0", "grade": 1 } ],
                  "faithfulness": { "expectedContextChunkIds": ["doc-a_chunk_0"] }
                }
              ]
            }
            """;

        var exception = Assert.Throws<InvalidDataException>(() => RetrievalEvaluationDatasetLoader.LoadFromJson(json));
        Assert.Contains("faithfulnessThresholds", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadFromJson_FaithfulnessExpectation_EmptyExpectedContextChunkIds_Throws()
    {
        const string json = """
            {
              "topK": 1,
              "thresholds": { "meanReciprocalRank": 0.5, "meanNdcgAtK": 0.5 },
              "faithfulnessThresholds": { "meanFaithfulness": 0.8, "minimumPerQueryFaithfulness": 0.6 },
              "documents": [ { "id": "doc-a", "content": "alpha" } ],
              "queries": [
                {
                  "id": "q1",
                  "text": "alpha",
                  "relevance": [ { "chunkId": "doc-a_chunk_0", "grade": 1 } ],
                  "faithfulness": { "expectedContextChunkIds": [] }
                }
              ]
            }
            """;
        Assert.Throws<InvalidDataException>(() => RetrievalEvaluationDatasetLoader.LoadFromJson(json));
    }

    [Fact]
    public void LoadFromJson_FaithfulnessExpectation_DuplicateChunkId_Throws()
    {
        const string json = """
            {
              "topK": 1,
              "thresholds": { "meanReciprocalRank": 0.5, "meanNdcgAtK": 0.5 },
              "faithfulnessThresholds": { "meanFaithfulness": 0.8, "minimumPerQueryFaithfulness": 0.6 },
              "documents": [ { "id": "doc-a", "content": "alpha" } ],
              "queries": [
                {
                  "id": "q1",
                  "text": "alpha",
                  "relevance": [ { "chunkId": "doc-a_chunk_0", "grade": 1 } ],
                  "faithfulness": { "expectedContextChunkIds": ["doc-a_chunk_0", "doc-a_chunk_0"] }
                }
              ]
            }
            """;
        Assert.Throws<InvalidDataException>(() => RetrievalEvaluationDatasetLoader.LoadFromJson(json));
    }

    [Fact]
    public void LoadFromJson_FaithfulnessExpectation_UnknownChunkId_Throws()
    {
        const string json = """
            {
              "topK": 1,
              "thresholds": { "meanReciprocalRank": 0.5, "meanNdcgAtK": 0.5 },
              "faithfulnessThresholds": { "meanFaithfulness": 0.8, "minimumPerQueryFaithfulness": 0.6 },
              "documents": [ { "id": "doc-a", "content": "alpha" } ],
              "queries": [
                {
                  "id": "q1",
                  "text": "alpha",
                  "relevance": [ { "chunkId": "doc-a_chunk_0", "grade": 1 } ],
                  "faithfulness": { "expectedContextChunkIds": ["doc-unknown_chunk_0"] }
                }
              ]
            }
            """;
        Assert.Throws<InvalidDataException>(() => RetrievalEvaluationDatasetLoader.LoadFromJson(json));
    }

    [Fact]
    public void LoadFromJson_NoFaithfulnessThresholdsAndNoExpectations_ParsesSuccessfully()
    {
        // Backwards compatibility: existing retrieval-only fixtures without faithfulness data are unaffected.
        const string json = """
            {
              "topK": 1,
              "thresholds": { "meanReciprocalRank": 0.5, "meanNdcgAtK": 0.5 },
              "documents": [ { "id": "doc-a", "content": "alpha" } ],
              "queries": [
                { "id": "q1", "text": "alpha", "relevance": [ { "chunkId": "doc-a_chunk_0", "grade": 1 } ] }
              ]
            }
            """;

        var dataset = RetrievalEvaluationDatasetLoader.LoadFromJson(json);

        Assert.Null(dataset.FaithfulnessThresholds);
        Assert.Null(Assert.Single(dataset.Queries).Faithfulness);
    }
}
