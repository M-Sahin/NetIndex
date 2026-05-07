using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Docx.Documents;
using NetIndex.Ingestion.Docx.Options;
using OpenXmlWordDoc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument;

namespace NetIndex.Ingestion.Docx.Loaders;

/// <summary>
/// Loads DOCX documents from streams, file paths, or directories,
/// extracting text from body paragraphs, tables, headers, and footers.
/// </summary>
public sealed class WordDocumentLoader : IDocumentLoader<DocxFormat>
{
    private readonly WordDocumentLoaderOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="WordDocumentLoader"/>.
    /// </summary>
    /// <param name="options">Loader configuration options.</param>
    public WordDocumentLoader(IOptions<WordDocumentLoaderOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <summary>
    /// Loads a document from the given DOCX stream.
    /// <see cref="IDocument.SourceUri"/> is <see langword="null"/> when loading from a bare stream.
    /// </summary>
    /// <param name="stream">The DOCX stream to load.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A populated <see cref="IDocument"/> with extracted text and metadata.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is null.</exception>
    public Task<IDocument> LoadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return LoadCoreAsync(stream, sourceUri: null, extraMetadata: null, cancellationToken);
    }

    /// <summary>
    /// Loads a document from the given DOCX file path.
    /// Sets <see cref="IDocument.SourceUri"/> to the absolute file URI and adds <c>file_name</c> to metadata.
    /// </summary>
    /// <param name="filePath">Path to the DOCX file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A populated <see cref="IDocument"/> with extracted text, metadata, and file URI.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filePath"/> is null, empty, or whitespace.</exception>
    public async Task<IDocument> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var extraMetadata = new Dictionary<string, string>
        {
            ["file_name"] = Path.GetFileName(filePath)
        };
        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        return await LoadCoreAsync(fileStream, DocxFileUri.Create(filePath), extraMetadata, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Enumerates all <c>.docx</c> files under <paramref name="directoryPath"/> and yields a loaded document per file.
    /// </summary>
    /// <param name="directoryPath">Directory to scan.</param>
    /// <param name="recursive">When <see langword="true"/>, descends into subdirectories.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async stream of <see cref="IDocument"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="directoryPath"/> is null, empty, or whitespace.</exception>
    public IAsyncEnumerable<IDocument> LoadDirectoryAsync(string directoryPath, bool recursive = true, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        return LoadDirectoryInternalAsync(directoryPath, recursive, cancellationToken);
    }

    private async IAsyncEnumerable<IDocument> LoadDirectoryInternalAsync(string directoryPath, bool recursive, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", searchOption))
        {
            if (!filePath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            cancellationToken.ThrowIfCancellationRequested();
            yield return await LoadAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IDocument> LoadCoreAsync(Stream stream, Uri? sourceUri, Dictionary<string, string>? extraMetadata, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await CopyWithSizeLimitAsync(stream, ms, _options.MaxInputSizeBytes, cancellationToken).ConfigureAwait(false);
        ms.Position = 0;

        using var wordDoc = OpenXmlWordDoc.Open(ms, isEditable: false);
        var mainPart = wordDoc.MainDocumentPart;

        if (mainPart?.Document?.Body is null)
        {
            return new WordDocument(Guid.NewGuid().ToString("N"), string.Empty, null, sourceUri);
        }

        var sb = new StringBuilder();
        var emittedCount = 0;

        // Body (including paragraphs nested in tables, SDT, etc.)
        foreach (var para in mainPart.Document.Body.Descendants<Paragraph>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = string.Concat(para.Descendants<Text>().Select(t => t.Text));
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text);
                emittedCount++;
            }
        }

        // Headers and footers
        foreach (var headerPart in mainPart.HeaderParts)
        {
            var header = headerPart.Header;
            if (header is null)
            {
                continue;
            }
            foreach (var para in header.Descendants<Paragraph>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = string.Concat(para.Descendants<Text>().Select(t => t.Text));
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine(text);
                    emittedCount++;
                }
            }
        }

        foreach (var footerPart in mainPart.FooterParts)
        {
            var footer = footerPart.Footer;
            if (footer is null)
            {
                continue;
            }
            foreach (var para in footer.Descendants<Paragraph>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = string.Concat(para.Descendants<Text>().Select(t => t.Text));
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine(text);
                    emittedCount++;
                }
            }
        }

        var content = sb.ToString();
        var props = wordDoc.PackageProperties;
        var dict = new Dictionary<string, string>
        {
            ["paragraph_count"] = emittedCount.ToString(CultureInfo.InvariantCulture)
        };

        AddIfPresent(dict, "title", props.Title);
        AddIfPresent(dict, "author", props.Creator);
        AddIfPresent(dict, "subject", props.Subject);
        AddIfPresent(dict, "description", props.Description);

        if (extraMetadata is not null)
        {
            foreach (var (key, value) in extraMetadata)
            {
                dict[key] = value;
            }
        }

        return new WordDocument(Guid.NewGuid().ToString("N"), content, dict, sourceUri);
    }

    private static async Task CopyWithSizeLimitAsync(Stream source, MemoryStream destination, long maxBytes, CancellationToken cancellationToken)
    {
        // For seekable streams a quick length check avoids reading at all.
        if (source.CanSeek && source.Length > maxBytes)
        {
            throw new InvalidDataException(
                $"DOCX input exceeds the configured limit of {maxBytes / (1024 * 1024)} MiB. " +
                "Increase WordDocumentLoaderOptions.MaxInputSizeBytes to allow larger files.");
        }

        var buffer = new byte[81920];
        long totalRead = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            totalRead += read;
            if (totalRead > maxBytes)
            {
                throw new InvalidDataException(
                    $"DOCX input exceeds the configured limit of {maxBytes / (1024 * 1024)} MiB. " +
                    "Increase WordDocumentLoaderOptions.MaxInputSizeBytes to allow larger files.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void AddIfPresent(Dictionary<string, string> dict, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            dict[key] = value;
        }
    }
}

/// <summary>
/// Builds file URIs that tolerate path characters reserved by the URI grammar (e.g. <c>#</c>, <c>%</c>).
/// </summary>
internal static class DocxFileUri
{
    public static Uri Create(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var escaped = fullPath
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace("#", "%23", StringComparison.Ordinal);
        return new Uri(new Uri("file:///"), escaped.Replace('\\', '/'));
    }
}
