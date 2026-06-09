using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Tesseract;
using NetIndex.Ingestion.Tesseract.Internal;
using NetIndex.Ingestion.Tesseract.Options;
using NetIndex.Ingestion.Tesseract.Tests.Coordinator;
using NetIndex.Testing.Common;
using MsOptions = Microsoft.Extensions.Options;
using Xunit;

namespace NetIndex.Ingestion.Tesseract.Tests.Native;

/// <summary>
/// Proves that <see cref="NetIndexOcrNotInstalledException"/> is thrown when the Tesseract
/// native library is absent. Run only in a clean environment without Tesseract installed.
/// </summary>
/// <remarks>
/// These tests use the internal test constructor with the real <see cref="TesseractOcrEngineFactory"/>
/// so that loading the native DLL is attempted on the first OCR call.
/// Use a fake rasterizer to avoid requiring PDFium as well.
/// </remarks>
[Trait("Category", "OcrNativeMissing")]
[Collection(TestingConstants.Collections.Tesseract)]
public sealed class OcrNativeMissingTests
{
    /// <summary>
    /// Verifies that calling ExtractAsync on a PNG stream throws NetIndexOcrNotInstalledException
    /// when the Tesseract native library is not present on the host.
    /// </summary>
    [Fact]
    public async Task ExtractAsync_NativeLibraryMissing_ThrowsOcrNotInstalledExceptionAsync()
    {
        var options = MsOptions.Options.Create(new TesseractOptions
        {
            TessDataPath = Path.GetTempPath(),
            Languages = "eng",
        });
        var rasterizer = new FakePdfRasterizer(pageCount: 1);
        var factory = new TesseractOcrEngineFactory(options.Value.TessDataPath, options.Value.Languages);
        await using var extractor = new TesseractVisionExtractor(options, rasterizer, factory);

        var act = async () => await extractor.ExtractAsync(
            new MemoryStream(
            [
                137, 80, 78, 71, 13, 10, 26, 10,
                0, 0, 0, 13, 73, 72, 68, 82,
                0, 0, 0, 1, 0, 0, 0, 1,
            ]),
            MediaTypes.Png);

        var ex = await act.Should().ThrowAsync<NetIndexOcrNotInstalledException>();
        ex.Which.RequiredPackage.Should().Be("NetIndex.Ingestion.Tesseract");
        ex.Which.InstallInstructions.Should().NotBeNullOrEmpty();
        ex.Which.InnerException.Should().NotBeNull("original native exception must be preserved");
    }
}
