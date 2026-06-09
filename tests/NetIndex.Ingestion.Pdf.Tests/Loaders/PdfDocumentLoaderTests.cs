using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Pdf.Loaders;
using NetIndex.Ingestion.Pdf.Options;
using MsOptions = Microsoft.Extensions.Options;
using Xunit;

namespace NetIndex.Ingestion.Pdf.Tests.Loaders;

/// <summary>
/// Unit tests for <see cref="PdfDocumentLoader"/>.
/// </summary>
public sealed class PdfDocumentLoaderTests
{
    private static Stream GetSamplePdfStream()
    {
        var assembly = typeof(PdfDocumentLoaderTests).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("sample-two-page.pdf", StringComparison.OrdinalIgnoreCase));
        return assembly.GetManifestResourceStream(resourceName)!;
    }

    private static PdfDocumentLoader CreateLoader(int threshold = 10, IVisionExtractor? extractor = null)
    {
        var options = MsOptions.Options.Create(new PdfDocumentLoaderOptions { MinimumTextPerPageThreshold = threshold });
        return new PdfDocumentLoader(options, extractor);
    }

    private static string WriteTempPdf()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netindex-test-{Guid.NewGuid():N}.pdf");
        using var src = GetSamplePdfStream();
        using var dst = File.Create(path);
        src.CopyTo(dst);
        return path;
    }

    /// <summary>
    /// Verifies that loading from a stream returns a document with non-empty content.
    /// </summary>
    [Fact]
    public async Task LoadAsync_FromStream_ReturnsDocumentWithContentAsync()
    {
        var loader = CreateLoader();
        using var stream = GetSamplePdfStream();

        var doc = await loader.LoadAsync(stream);

        doc.Content.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Verifies that passing a null stream throws <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public Task LoadAsync_NullStream_ThrowsArgumentNullExceptionAsync()
    {
        var loader = CreateLoader();

        var act = () => loader.LoadAsync((Stream)null!);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// Verifies that the page_count metadata key is present and parseable.
    /// </summary>
    [Fact]
    public async Task LoadAsync_FromStream_MetadataContainsPageCountAsync()
    {
        var loader = CreateLoader();
        using var stream = GetSamplePdfStream();

        var doc = await loader.LoadAsync(stream);

        doc.Metadata.Should().ContainKey("page_count");
        int.Parse(doc.Metadata!["page_count"]).Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Verifies that SourceUri is null when loading from a bare stream.
    /// </summary>
    [Fact]
    public async Task LoadAsync_FromStream_SourceUriIsNullAsync()
    {
        var loader = CreateLoader();
        using var stream = GetSamplePdfStream();

        var doc = await loader.LoadAsync(stream);

        doc.SourceUri.Should().BeNull();
    }

    /// <summary>
    /// Verifies that SourceUri is set to the file URI when loading from a file path.
    /// </summary>
    [Fact]
    public async Task LoadAsync_FromFilePath_SetsSourceUriAsync()
    {
        var loader = CreateLoader();
        var path = WriteTempPdf();
        try
        {
            var doc = await loader.LoadAsync(path);

            doc.SourceUri.Should().NotBeNull();
            doc.SourceUri!.IsFile.Should().BeTrue();
            doc.SourceUri.LocalPath.Should().Contain(Path.GetFileName(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Verifies that the file_name metadata key matches the file name when loading from a file path.
    /// </summary>
    [Fact]
    public async Task LoadAsync_FromFilePath_MetadataContainsFileNameAsync()
    {
        var loader = CreateLoader();
        var path = WriteTempPdf();
        try
        {
            var doc = await loader.LoadAsync(path);

            doc.Metadata.Should().ContainKey("file_name");
            doc.Metadata!["file_name"].Should().Be(Path.GetFileName(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Verifies that a scanned (image-only) PDF triggers <see cref="NetIndexOcrNotInstalledException"/>.
    /// </summary>
    [Fact]
    public async Task LoadAsync_ScannedPdf_ThrowsNetIndexOcrNotInstalledExceptionAsync()
    {
        var loader = CreateLoader(threshold: int.MaxValue);
        using var stream = GetSamplePdfStream();

        var act = () => loader.LoadAsync(stream);

        await act.Should().ThrowAsync<NetIndexOcrNotInstalledException>()
            .Where(ex => ex.RequiredPackage == "NetIndex.Ingestion.Tesseract");
    }

    /// <summary>
    /// Verifies that a pre-cancelled token causes <see cref="OperationCanceledException"/>.
    /// </summary>
    [Fact]
    public async Task LoadAsync_CancellationRequested_ThrowsOperationCanceledExceptionAsync()
    {
        var loader = CreateLoader();
        using var stream = GetSamplePdfStream();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => loader.LoadAsync(stream, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>Verifies low-text PDFs rewind and delegate through the managed vision contract.</summary>
    [Fact]
    public async Task LoadAsync_LowTextPdf_DelegatesToVisionExtractorAsync()
    {
        var extractor = new RecordingVisionExtractor();
        var loader = CreateLoader(int.MaxValue, extractor);
        using var stream = GetSamplePdfStream();

        var document = await loader.LoadAsync(stream);

        extractor.CallCount.Should().Be(1);
        extractor.PositionAtCall.Should().Be(0);
        extractor.MediaType.Should().Be(MediaTypes.Pdf);
        document.Content.Should().Be("ocr text");
        document.Metadata.Should().Contain(new Dictionary<string, string>
        {
            ["ocr_engine"] = "tesseract",
            ["ocr_engine_version"] = "5.test",
            ["ocr_language"] = "eng",
            ["ocr_mean_confidence"] = "0.750000",
            ["ocr_page_count"] = "2",
            ["ocr_dpi"] = "300",
        });
    }

    /// <summary>Verifies text-first extraction does not invoke OCR.</summary>
    [Fact]
    public async Task LoadAsync_TextPdf_SkipsVisionExtractorAsync()
    {
        var extractor = new RecordingVisionExtractor();
        var loader = CreateLoader(0, extractor);
        using var stream = GetSamplePdfStream();

        var document = await loader.LoadAsync(stream);

        document.Content.Should().NotBe("ocr text");
        extractor.CallCount.Should().Be(0);
    }

    /// <summary>Verifies OCR file loading preserves source URI and file metadata.</summary>
    [Fact]
    public async Task LoadAsync_OcrFromFile_PreservesUriAndMetadataAsync()
    {
        var extractor = new RecordingVisionExtractor();
        var loader = CreateLoader(int.MaxValue, extractor);
        var path = WriteTempPdf();
        try
        {
            var document = await loader.LoadAsync(path);

            document.SourceUri!.LocalPath.Should().Be(Path.GetFullPath(path));
            document.Metadata!["file_name"].Should().Be(Path.GetFileName(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Verifies OCR directory loading retains normal directory behavior.</summary>
    [Fact]
    public async Task LoadDirectoryAsync_OcrPdf_YieldsDocumentAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netindex-pdf-dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "scan.pdf");
        using (var source = GetSamplePdfStream())
        using (var destination = File.Create(path))
        {
            await source.CopyToAsync(destination);
        }

        try
        {
            var loader = CreateLoader(int.MaxValue, new RecordingVisionExtractor());
            var documents = new List<IDocument>();
            await foreach (var document in loader.LoadDirectoryAsync(directory, recursive: false))
            {
                documents.Add(document);
            }

            documents.Should().ContainSingle();
            documents[0].SourceUri!.LocalPath.Should().Be(path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies cancellation raised by the extractor propagates unchanged.</summary>
    [Fact]
    public async Task LoadAsync_OcrCancellation_PropagatesAsync()
    {
        using var cts = new CancellationTokenSource();
        var extractor = new RecordingVisionExtractor(() => _ = cts.CancelAsync());
        var loader = CreateLoader(int.MaxValue, extractor);
        using var stream = GetSamplePdfStream();

        var act = () => loader.LoadAsync(stream, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        extractor.CancellationToken.Should().Be(cts.Token);
    }
}

internal sealed class RecordingVisionExtractor(Action? onExtract = null) : IVisionExtractor
{
    internal int CallCount { get; private set; }
    internal long PositionAtCall { get; private set; }
    internal string? MediaType { get; private set; }
    internal CancellationToken CancellationToken { get; private set; }

    public Task<VisionExtractionResult> ExtractAsync(
        Stream source,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        PositionAtCall = source.Position;
        MediaType = mediaType;
        CancellationToken = cancellationToken;
        onExtract?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();

        VisionPageResult[] pages =
        [
            new(1, "ocr page one", 0.8),
            new(2, "ocr page two", 0.7),
        ];
        return Task.FromResult(new VisionExtractionResult(
            "ocr text",
            0.75,
            pages,
            "tesseract",
            "5.test",
            "eng",
            300));
    }
}
