using System;
using System.Collections.Generic;
using System.Globalization;
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
/// When extracted text falls below the configured threshold the loader delegates
/// to an optional <see cref="IVisionExtractor"/> for OCR-based extraction.
/// </summary>
public sealed class PdfDocumentLoader : IDocumentLoader<PdfFormat>
{
    private readonly PdfDocumentLoaderOptions _options;
    private readonly IVisionExtractor? _visionExtractor;

    /// <summary>
    /// Initializes a new instance of <see cref="PdfDocumentLoader"/> without OCR support.
    /// This overload exists for binary compatibility; applications that need OCR should
    /// register <see cref="IVisionExtractor"/> in the DI container (e.g., via
    /// <c>builder.UseTesseract()</c>) so the two-parameter constructor is used.
    /// </summary>
    /// <param name="options">Loader configuration options.</param>
    public PdfDocumentLoader(IOptions<PdfDocumentLoaderOptions> options)
        : this(options, extractor: null) { }

    /// <summary>
    /// Initializes a new instance of <see cref="PdfDocumentLoader"/> with optional OCR support.
    /// </summary>
    /// <param name="options">Loader configuration options.</param>
    /// <param name="extractor">
    /// Optional vision extractor used when extracted text is below the threshold.
    /// When <see langword="null"/>, a scanned PDF throws <see cref="NetIndexOcrNotInstalledException"/>.
    /// </param>
    public PdfDocumentLoader(IOptions<PdfDocumentLoaderOptions> options, IVisionExtractor? extractor)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _visionExtractor = extractor;
    }

    /// <summary>
    /// Loads a document from the given PDF stream.
    /// Returns a <see cref="Documents.PdfDocument"/> with extracted text and metadata;
    /// <see cref="IDocument.SourceUri"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="stream">The raw PDF stream to parse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A populated <see cref="IDocument"/> with extracted text and metadata.</returns>
    /// <exception cref="NetIndexOcrNotInstalledException">
    /// Thrown when the average characters per page falls below
    /// <see cref="PdfDocumentLoaderOptions.MinimumTextPerPageThreshold"/> and no
    /// <see cref="IVisionExtractor"/> is configured.
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
            if (_visionExtractor is null)
            {
                throw new NetIndexOcrNotInstalledException(
                    "PDF appears to contain scanned images with no extractable text. Add NetIndex.Ingestion.Tesseract for OCR support.",
                    requiredPackage: "NetIndex.Ingestion.Tesseract",
                    installInstructions: "dotnet add package NetIndex.Ingestion.Tesseract");
            }

            ms.Position = 0;
            var ocrResult = await _visionExtractor
                .ExtractAsync(ms, MediaTypes.Pdf, cancellationToken)
                .ConfigureAwait(false);

            var ocrMeta = BuildBaseMetadata(pageCount, info, extraMetadata);
            AddOcrMetadata(ocrMeta, ocrResult);
            return new PdfDocument(Guid.NewGuid().ToString("N"), ocrResult.Text, ocrMeta, sourceUri);
        }

        var dict = BuildBaseMetadata(pageCount, info, extraMetadata);
        return new PdfDocument(Guid.NewGuid().ToString("N"), fullText, dict, sourceUri);
    }

    private static Dictionary<string, string> BuildBaseMetadata(
        int pageCount,
        UglyToad.PdfPig.Content.DocumentInformation info,
        Dictionary<string, string>? extraMetadata)
    {
        var dict = new Dictionary<string, string>
        {
            ["page_count"] = pageCount.ToString(CultureInfo.InvariantCulture)
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
        return dict;
    }

    private static void AddOcrMetadata(Dictionary<string, string> dict, VisionExtractionResult ocrResult)
    {
        dict["ocr_engine"] = ocrResult.EngineName;
        dict["ocr_engine_version"] = ocrResult.EngineVersion;
        dict["ocr_language"] = ocrResult.Language;
        dict["ocr_mean_confidence"] = ocrResult.MeanConfidence.ToString("F6", CultureInfo.InvariantCulture);
        dict["ocr_page_count"] = ocrResult.Pages.Count.ToString(CultureInfo.InvariantCulture);
        dict["ocr_dpi"] = ocrResult.RasterizationDpi.ToString(CultureInfo.InvariantCulture);
    }

    private static void AddIfPresent(Dictionary<string, string> dict, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            dict[key] = value;
        }
    }
}
