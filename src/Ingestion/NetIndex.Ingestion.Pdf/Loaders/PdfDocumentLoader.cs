using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Pdf.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using PdfDocument = NetIndex.Ingestion.Pdf.Documents.PdfDocument;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace NetIndex.Ingestion.Pdf.Loaders;

/// <summary>
/// Loads PDF documents by extracting text and metadata using PdfPig.
/// </summary>
public sealed class PdfDocumentLoader : IDocumentLoader<PdfFormat>
{
    private readonly PdfDocumentLoaderOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="PdfDocumentLoader"/>.
    /// </summary>
    /// <param name="options">Loader configuration options.</param>
    public PdfDocumentLoader(IOptions<PdfDocumentLoaderOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <summary>
    /// Loads a document from the given PDF stream.
    /// Returns a <see cref="PdfDocument"/> with extracted text and metadata; <see cref="IDocument.SourceUri"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="stream">The raw PDF stream to parse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A populated <see cref="IDocument"/> with extracted text and metadata.</returns>
    /// <exception cref="NetIndexOcrNotInstalledException">
    /// Thrown when the average characters per page falls below <see cref="PdfDocumentLoaderOptions.MinimumTextPerPageThreshold"/>,
    /// indicating a scanned PDF that requires OCR.
    /// </exception>
    public Task<IDocument> LoadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return LoadCoreAsync(stream, sourceUri: null, extraMetadata: null, cancellationToken);
    }

    /// <summary>
    /// Loads a document from the given file path.
    /// Sets <see cref="IDocument.SourceUri"/> to the full file path URI and adds <c>file_name</c> to metadata.
    /// </summary>
    /// <param name="filePath">Absolute or relative path to the PDF file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A populated <see cref="IDocument"/> with extracted text, metadata, and file URI.</returns>
    public async Task<IDocument> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        await using var fileStream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);

        var extraMetadata = new Dictionary<string, string>
        {
            ["file_name"] = Path.GetFileName(filePath)
        };

        return await LoadCoreAsync(fileStream, new Uri(Path.GetFullPath(filePath)), extraMetadata, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Enumerates all <c>.pdf</c> files under <paramref name="directoryPath"/> and yields a loaded document per file.
    /// </summary>
    /// <param name="directoryPath">Directory to scan.</param>
    /// <param name="recursive">When <see langword="true"/>, descends into subdirectories.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async stream of <see cref="IDocument"/>.</returns>
    public IAsyncEnumerable<IDocument> LoadDirectoryAsync(string directoryPath, bool recursive = true, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        return LoadDirectoryInternalAsync(directoryPath, recursive, cancellationToken);
    }

    private async IAsyncEnumerable<IDocument> LoadDirectoryInternalAsync(
        string directoryPath,
        bool recursive,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", searchOption))
        {
            if (!filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            cancellationToken.ThrowIfCancellationRequested();
            yield return await LoadAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IDocument> LoadCoreAsync(
        Stream stream,
        Uri? sourceUri,
        Dictionary<string, string>? extraMetadata,
        CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        ms.Position = 0;

        using var pdfDoc = PdfPigDocument.Open(ms);
        var pageCount = pdfDoc.NumberOfPages;
        var info = pdfDoc.Information;

        var sb = new StringBuilder();
        foreach (var page in pdfDoc.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            sb.AppendLine(ContentOrderTextExtractor.GetText(page));
        }

        var fullText = sb.ToString();

        var averageCharsPerPage = pageCount > 0 ? fullText.Length / pageCount : 0;
        if (pageCount > 0 && averageCharsPerPage < _options.MinimumTextPerPageThreshold)
        {
            throw new NetIndexOcrNotInstalledException(
                "PDF appears to contain scanned images with no extractable text. Add NetIndex.Ingestion.Tesseract for OCR support.",
                requiredPackage: "NetIndex.Ingestion.Tesseract",
                installInstructions: "dotnet add package NetIndex.Ingestion.Tesseract");
        }

        var dict = new Dictionary<string, string>
        {
            ["page_count"] = pageCount.ToString()
        };
        AddIfPresent(dict, "title", info.Title);
        AddIfPresent(dict, "author", info.Author);
        AddIfPresent(dict, "subject", info.Subject);
        AddIfPresent(dict, "creator", info.Creator);

        if (extraMetadata is not null)
        {
            foreach (var (key, value) in extraMetadata)
            {
                dict[key] = value;
            }
        }

        return new PdfDocument(Guid.NewGuid().ToString("N"), fullText, dict, sourceUri);
    }

    private static void AddIfPresent(Dictionary<string, string> dict, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            dict[key] = value;
        }
    }
}
