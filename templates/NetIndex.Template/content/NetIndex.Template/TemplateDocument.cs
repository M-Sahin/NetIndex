using NetIndex.Core.Abstractions;

namespace NetIndex.Template;

/// <summary>
/// Minimal <see cref="IDocument"/> implementation used by the scaffolded
/// <c>/ingest</c> endpoint.
/// </summary>
/// <remarks>
/// Story 4.4 will evolve this to a richer shape. Keep it minimal here.
/// </remarks>
/// <param name="Id">Unique document identifier supplied by the caller.</param>
/// <param name="Content">Full text content to chunk, embed, and store.</param>
internal sealed record TemplateDocument(
    string Id,
    string Content) : IDocument
{
    /// <inheritdoc />
    public IReadOnlyDictionary<string, string>? Metadata => null;

    /// <inheritdoc />
    public Uri? SourceUri => null;
}
