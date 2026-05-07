using System;
using System.Collections.Generic;
using NetIndex.Core.Abstractions;

namespace NetIndex.Ingestion.Markdown.Documents;

/// <summary>
/// An <see cref="IDocument"/> produced by <see cref="Loaders.MarkdownDocumentLoader"/> from a Markdown source.
/// </summary>
/// <param name="Id">Unique identifier for this document instance.</param>
/// <param name="Content">Markdown body text with front matter removed.</param>
/// <param name="Metadata">Optional metadata dictionary (has_front_matter, parsed YAML front matter key/value pairs, and file_name from file-path overload).</param>
/// <param name="SourceUri">File URI when loaded from a path; <see langword="null"/> when loaded from a bare stream.</param>
public sealed record MarkdownDocument(
    string Id,
    string Content,
    IReadOnlyDictionary<string, string>? Metadata,
    Uri? SourceUri) : IDocument;