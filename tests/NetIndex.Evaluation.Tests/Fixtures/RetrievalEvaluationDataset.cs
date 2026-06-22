namespace NetIndex.Evaluation.Tests.Fixtures;

/// <summary>
/// Version-controlled retrieval evaluation dataset: a knowledge base, a query set, and pass/fail thresholds.
/// </summary>
internal sealed record RetrievalEvaluationDataset(
    int TopK,
    RetrievalEvaluationThresholds Thresholds,
    IReadOnlyList<RetrievalEvaluationDocument> Documents,
    IReadOnlyList<RetrievalEvaluationQuery> Queries);

/// <summary>
/// Aggregate pass/fail thresholds for the committed dataset, each an explicit value in [0, 1].
/// </summary>
internal sealed record RetrievalEvaluationThresholds(
    double MeanReciprocalRank,
    double MeanNdcgAtK);

/// <summary>
/// A single document in the evaluation knowledge base, identified by a stable, unique <see cref="Id"/>.
/// </summary>
internal sealed record RetrievalEvaluationDocument(
    string Id,
    string Content);

/// <summary>
/// An evaluation query together with its ground-truth relevance judgments.
/// </summary>
internal sealed record RetrievalEvaluationQuery(
    string Id,
    string Text,
    IReadOnlyList<RetrievalRelevanceJudgment> Relevance);

/// <summary>
/// A single ground-truth relevance judgment keyed by the expected <c>RagChunk.Id</c>.
/// </summary>
/// <remarks>
/// Grade is an integer in [0, 3]: 0 means not relevant, values &gt;= 1 mean relevant.
/// </remarks>
internal sealed record RetrievalRelevanceJudgment(
    string ChunkId,
    int Grade);
