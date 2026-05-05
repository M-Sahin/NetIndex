using System;
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

    private static PdfDocumentLoader CreateLoader(int threshold = 10)
    {
        var options = MsOptions.Options.Create(new PdfDocumentLoaderOptions { MinimumTextPerPageThreshold = threshold });
        return new PdfDocumentLoader(options);
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
}
