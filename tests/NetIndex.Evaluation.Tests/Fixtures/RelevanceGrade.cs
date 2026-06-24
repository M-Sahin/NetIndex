namespace NetIndex.Evaluation.Tests.Fixtures;

/// <summary>
/// Single source of truth for the inclusive relevance-grade range shared by the dataset loader
/// (which validates committed judgments) and the metrics (which re-validate at compute time).
/// Duplicating these bounds per file silently breaks one validator if the other's cap changes.
/// </summary>
internal static class RelevanceGrade
{
    /// <summary>Lowest valid relevance grade (0 = not relevant).</summary>
    public const int Min = 0;

    /// <summary>Highest valid relevance grade (3 = maximally relevant).</summary>
    public const int Max = 3;
}
