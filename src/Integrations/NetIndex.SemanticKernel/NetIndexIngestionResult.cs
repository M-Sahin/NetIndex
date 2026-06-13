using System.ComponentModel;

namespace NetIndex.SemanticKernel;

/// <summary>
/// The result of the <c>IngestDocument</c> plugin function.
/// </summary>
/// <param name="DocumentId">The identifier of the document that was ingested.</param>
public sealed record NetIndexIngestionResult(
    [property: Description("The identifier of the document that was ingested.")]
    string DocumentId);
