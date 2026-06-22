using NetIndex.Core.Abstractions;

namespace NetIndex.Evaluation.Tests.TestSupport;

/// <summary>
/// Minimal immutable <see cref="IDocument"/> wrapping an evaluation fixture document.
/// </summary>
internal sealed class EvaluationDocument(string id, string content) : IDocument
{
    public string Id { get; } = id;

    public string Content { get; } = content;

    public IReadOnlyDictionary<string, string>? Metadata => null;

    public Uri? SourceUri => null;
}
