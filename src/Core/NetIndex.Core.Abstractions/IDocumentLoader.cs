using System.Collections.Generic;
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
    /// <see cref="IDocument.SourceUri"/> is <see langword="null"/> when loading from a bare stream.
    /// </summary>
    /// <param name="stream">The raw file stream to parse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A populated <see cref="IDocument"/> with extracted text and metadata.</returns>
    Task<IDocument> LoadAsync(Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a document from the given file path.
    /// Sets <see cref="IDocument.SourceUri"/> to the absolute file URI and adds <c>file_name</c> to metadata.
    /// </summary>
    /// <param name="filePath">Path to the file to load.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A populated <see cref="IDocument"/> with extracted text, metadata, and file URI.</returns>
    Task<IDocument> LoadAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates all matching files under <paramref name="directoryPath"/> and yields a loaded <see cref="IDocument"/> per file.
    /// </summary>
    /// <param name="directoryPath">Directory to scan.</param>
    /// <param name="recursive">When <see langword="true"/>, descends into subdirectories.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async stream of <see cref="IDocument"/>.</returns>
    IAsyncEnumerable<IDocument> LoadDirectoryAsync(string directoryPath, bool recursive = true, CancellationToken cancellationToken = default);
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
