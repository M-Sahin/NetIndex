using System;
using System.Collections.Generic;
using NetIndex.Core.Abstractions;

namespace NetIndex.Ingestion.Docx.Documents;

/// <summary>
/// An <see cref="IDocument"/> produced by <see cref="Loaders.WordDocumentLoader"/> from a DOCX source.
/// </summary>
/// <param name="Id">Unique identifier for this document instance.</param>
/// <param name="Content">Full extracted paragraph text, one non-empty paragraph per line.</param>
/// <param name="Metadata">Optional metadata dictionary (paragraph_count, title, author, subject, description, file_name).</param>
/// <param name="SourceUri">File URI when loaded from a path; <see langword="null"/> when loaded from a bare stream.</param>
public sealed record WordDocument(
    string Id,
    string Content,
    IReadOnlyDictionary<string, string>? Metadata,
    Uri? SourceUri) : IDocument;