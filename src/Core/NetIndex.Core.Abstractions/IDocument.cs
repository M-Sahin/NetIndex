namespace NetIndex.Core.Abstractions;

/// <summary>
/// Base document interface representing an ingested source document.
/// </summary>
/// <remarks>
/// Canonical noun #2 in NOUNS.md.
/// 
/// Use <see cref="IDocument{TMetadata}"/> when typed metadata is required.
/// This non-generic variant exists for scenarios where metadata shape is unknown at compile time.
/// </remarks>
public interface IDocument
{
    /// <summary>
    /// Gets the unique identifier of this document.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the full text content of the document.
    /// </summary>
    string Content { get; }

    /// <summary>
    /// Gets the metadata associated with this document as an untyped dictionary.
    /// </summary>
    /// <remarks>
    /// Use <see cref="IDocument{TMetadata}"/> for strongly-typed metadata access.
    /// </remarks>
    IReadOnlyDictionary<string, string>? Metadata { get; }

    /// <summary>
    /// Gets the URI or path of the original source file.
    /// </summary>
    /// <remarks>
    /// May be a file path, URL, or other identifier depending on the loader implementation.
    /// </remarks>
    Uri? SourceUri { get; }
}

/// <summary>
/// Typed document interface with generic metadata.
/// </summary>
/// <typeparam name="TMetadata">The type of metadata attached to this document.</typeparam>
/// <remarks>
/// Canonical noun #2 in NOUNS.md. Extends <see cref="IDocument"/> with a strongly-typed metadata property.
/// </remarks>
public interface IDocument<TMetadata> : IDocument
{
    /// <summary>
    /// Gets the strongly-typed metadata attached to this document.
    /// </summary>
    TMetadata? TypedMetadata { get; }
}
