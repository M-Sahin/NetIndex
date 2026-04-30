using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NetIndex.Core.Abstractions;

/// <summary>
/// Loads documents from a specific format into an <see cref="IDocument"/>.
/// </summary>
/// <typeparam name="TFormat">A marker type indicating the supported format (e.g., <c>PdfFormat</c>, <c>DocxFormat</c>).</typeparam>
/// <remarks>
/// Canonical noun #11 (Loader) in NOUNS.md.
/// 
/// Implementations live in <c>NetIndex.Ingestion.*</c> packages (one package per format).
/// Each loader reads the raw stream, parses the format-specific structure, and extracts
/// text content plus metadata into an <see cref="IDocument"/>.
/// </remarks>
public interface IDocumentLoader<TFormat>
{
    /// <summary>
    /// Loads a document from the given stream.
    /// </summary>
    /// <param name="stream">The raw file stream to parse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A populated <see cref="IDocument"/> with extracted text and metadata.</returns>
    Task<IDocument> LoadAsync(Stream stream, CancellationToken cancellationToken = default);
}

/// <summary>
/// Marker type for PDF format loaders.
/// </summary>
public sealed record PdfFormat;

/// <summary>
/// Marker type for DOCX format loaders.
/// </summary>
public sealed record DocxFormat;

/// <summary>
/// Marker type for Markdown format loaders.
/// </summary>
public sealed record MarkdownFormat;
