using System;
using System.Collections.Generic;
using NetIndex.Core.Abstractions;

namespace NetIndex.Ingestion.Pdf.Documents;

/// <summary>
/// An <see cref="IDocument"/> produced by <see cref="Loaders.PdfDocumentLoader"/> from a PDF source.
/// </summary>
/// <param name="Id">Unique identifier for this document instance.</param>
/// <param name="Content">Full extracted text from all pages, joined by newlines.</param>
/// <param name="Metadata">Optional metadata dictionary (page_count, title, author, etc.).</param>
/// <param name="SourceUri">File URI when loaded from a path; <see langword="null"/> when loaded from a bare stream.</param>
public sealed record PdfDocument(
    string Id,
    string Content,
    IReadOnlyDictionary<string, string>? Metadata,
    Uri? SourceUri) : IDocument;
