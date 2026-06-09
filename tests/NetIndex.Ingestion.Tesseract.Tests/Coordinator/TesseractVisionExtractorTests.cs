using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Tesseract;
using NetIndex.Ingestion.Tesseract.Internal;
using NetIndex.Ingestion.Tesseract.Options;
using Xunit;

namespace NetIndex.Ingestion.Tesseract.Tests.Coordinator;

internal sealed class FakePdfRasterizer : IPdfPageRasterizer
{
    private readonly int _pageCount;
    private readonly long _pixels;
    private readonly Action? _onRasterize;
    private Exception? _throwOnPageCount;
    private Exception? _throwOnRasterize;

    internal FakePdfRasterizer(int pageCount = 1, long pixels = 100, Action? onRasterize = null)
    {
        _pageCount = pageCount;
        _pixels = pixels;
        _onRasterize = onRasterize;
    }

    internal int RasterizeCount { get; private set; }
    internal TrackingMemoryStream? LastRenderedStream { get; private set; }
    internal void ThrowOnPageCount(Exception ex) => _throwOnPageCount = ex;
    internal void ThrowOnRasterize(Exception ex) => _throwOnRasterize = ex;

    public Task<int> GetPageCountAsync(Stream pdfStream, CancellationToken cancellationToken = default)
    {
        if (_throwOnPageCount is not null)
        {
            throw _throwOnPageCount;
        }
        return Task.FromResult(_pageCount);
    }

    public Task<long> GetPagePixelCountAsync(
        Stream pdfStream, int pageIndex, int dpi, CancellationToken cancellationToken = default)
        => Task.FromResult(_pixels);

    public Task<Stream> RasterizePageAsync(
        Stream pdfStream, int pageIndex, int dpi, CancellationToken cancellationToken = default)
    {
        if (_throwOnRasterize is not null)
        {
            throw _throwOnRasterize;
        }
        RasterizeCount++;
        LastRenderedStream = new TrackingMemoryStream([0x01]);
        _onRasterize?.Invoke();
        return Task.FromResult<Stream>(LastRenderedStream);
    }
}

internal sealed class TrackingMemoryStream(byte[] bytes) : MemoryStream(bytes)
{
    internal bool IsDisposed { get; private set; }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}

/// <summary>
/// Managed tests for <see cref="TesseractVisionExtractor"/>. No native Tesseract library required.
/// All internal adapters are replaced with controllable fakes via the internal test constructor.
/// </summary>
public sealed class TesseractVisionExtractorTests
{
    // -------------------------------------------------------------------------
    // Unsupported media type
    // -------------------------------------------------------------------------

    /// <summary>Verifies that an unsupported media type throws NetIndexProviderException with the correct error code.</summary>
    [Fact]
    public async Task ExtractAsync_UnsupportedMediaType_ThrowsProviderExceptionAsync()
    {
        var (sut, _, _) = MakeExtractor();
        await using (sut)
        {
            var act = async () => await sut.ExtractAsync(new MemoryStream([0x01]), "application/octet-stream");

            var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
            ex.Which.ErrorCode.Should().Be("unsupported_media_type");
            ex.Which.ProviderName.Should().Be("Tesseract");
            ex.Which.IsRetryable.Should().BeFalse();
        }
    }

    // -------------------------------------------------------------------------
    // Input size limit
    // -------------------------------------------------------------------------

    /// <summary>Verifies that a stream exceeding MaxInputBytes throws with error code input_too_large.</summary>
    [Fact]
    public async Task ExtractAsync_InputTooLarge_ThrowsProviderExceptionAsync()
    {
        var (sut, _, _) = MakeExtractor(maxInputBytes: 5);
        await using (sut)
        {
            var act = async () => await sut.ExtractAsync(new MemoryStream([1, 2, 3, 4, 5, 6]), MediaTypes.Pdf);

            var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
            ex.Which.ErrorCode.Should().Be("input_too_large");
        }
    }

    // -------------------------------------------------------------------------
    // Page limit
    // -------------------------------------------------------------------------

    /// <summary>Verifies that a PDF with more pages than MaxPages throws with error code page_limit_exceeded.</summary>
    [Fact]
    public async Task ExtractAsync_PageLimitExceeded_ThrowsProviderExceptionAsync()
    {
        var rasterizer = new FakePdfRasterizer(pageCount: 5);
        var (sut, _, _) = MakeExtractor(rasterizer: rasterizer, maxPages: 3);
        await using (sut)
        {
            var act = async () => await sut.ExtractAsync(new MemoryStream([1]), MediaTypes.Pdf);

            var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
            ex.Which.ErrorCode.Should().Be("page_limit_exceeded");
        }
    }

    // -------------------------------------------------------------------------
    // Rasterization failure
    // -------------------------------------------------------------------------

    /// <summary>Verifies that a rasterization exception is wrapped in a NetIndexProviderException with render_failed.</summary>
    [Fact]
    public async Task ExtractAsync_RasterizationFails_ThrowsProviderExceptionAsync()
    {
        var rasterizer = new FakePdfRasterizer(pageCount: 1);
        rasterizer.ThrowOnRasterize(new InvalidOperationException("PDFium crashed"));
        var (sut, _, _) = MakeExtractor(rasterizer: rasterizer);
        await using (sut)
        {
            var act = async () => await sut.ExtractAsync(new MemoryStream([1]), MediaTypes.Pdf);

            var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
            ex.Which.ErrorCode.Should().Be("render_failed");
            ex.Which.InnerException.Should().BeOfType<InvalidOperationException>();
        }
    }

    // -------------------------------------------------------------------------
    // Pixel limit
    // -------------------------------------------------------------------------

    /// <summary>Verifies that a page exceeding MaxPixelsPerPage throws with error code pixel_limit_exceeded.</summary>
    [Fact]
    public async Task ExtractAsync_PixelLimitExceeded_ThrowsProviderExceptionAsync()
    {
        var rasterizer = new FakePdfRasterizer(pageCount: 1, pixels: 200);
        var (sut, _, _) = MakeExtractor(rasterizer: rasterizer, maxPixelsPerPage: 100);
        await using (sut)
        {
            var act = async () => await sut.ExtractAsync(new MemoryStream([1]), MediaTypes.Pdf);

            var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
            ex.Which.ErrorCode.Should().Be("pixel_limit_exceeded");
            rasterizer.RasterizeCount.Should().Be(0, "the bound must be enforced before rendering");
        }
    }

    // -------------------------------------------------------------------------
    // Recognition failure
    // -------------------------------------------------------------------------

    /// <summary>Verifies that a recognition exception is wrapped with error code recognition_failed.</summary>
    [Fact]
    public async Task ExtractAsync_RecognitionFails_ThrowsProviderExceptionAsync()
    {
        var engine = new FakeOcrEngine();
        engine.ThrowOnRecognize(new Exception("native crash"));
        var (sut, _, _) = MakeExtractor(engine: engine);
        await using (sut)
        {
            var act = async () => await sut.ExtractAsync(CreateJpegStream(), MediaTypes.Jpeg);

            var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
            ex.Which.ErrorCode.Should().Be("recognition_failed");
            ex.Which.InnerException!.Message.Should().Contain("native crash");
        }
    }

    // -------------------------------------------------------------------------
    // Empty OCR result
    // -------------------------------------------------------------------------

    /// <summary>Verifies that all-whitespace OCR output throws with error code empty_ocr_result.</summary>
    [Fact]
    public async Task ExtractAsync_WhitespaceOnlyResult_ThrowsProviderExceptionAsync()
    {
        var engine = new FakeOcrEngine("   \n\t  ", confidence: 50f);
        var (sut, _, _) = MakeExtractor(engine: engine);
        await using (sut)
        {
            var act = async () => await sut.ExtractAsync(CreatePngStream(), MediaTypes.Png);

            var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
            ex.Which.ErrorCode.Should().Be("empty_ocr_result");
        }
    }

    // -------------------------------------------------------------------------
    // Cancellation
    // -------------------------------------------------------------------------

    /// <summary>Verifies that a pre-cancelled token causes OperationCanceledException before page work begins.</summary>
    [Fact]
    public async Task ExtractAsync_CancelledToken_ThrowsOperationCancelledAsync()
    {
        var (sut, _, _) = MakeExtractor();
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await using (sut)
        {
            var act = async () => await sut.ExtractAsync(CreatePngStream(), MediaTypes.Png, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
    }

    /// <summary>Verifies that cancellation between pages stops processing and propagates correctly.</summary>
    [Fact]
    public async Task ExtractAsync_CancelledBetweenPages_ThrowsOperationCancelledAsync()
    {
        var cts = new CancellationTokenSource();
        var engine = new FakeOcrEngine("page text", confidence: 80f, onRecognize: () => _ = cts.CancelAsync());
        var rasterizer = new FakePdfRasterizer(pageCount: 2);
        var (sut, _, _) = MakeExtractor(rasterizer: rasterizer, engine: engine);
        await using (sut)
        {
            var act = async () => await sut.ExtractAsync(new MemoryStream([1]), MediaTypes.Pdf, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
    }

    // -------------------------------------------------------------------------
    // Happy path: page order and text combination
    // -------------------------------------------------------------------------

    /// <summary>Verifies that pages are returned in source order and text is combined with newlines.</summary>
    [Fact]
    public async Task ExtractAsync_MultiPagePdf_ReturnsOrderedPagesAsync()
    {
        var rasterizer = new FakePdfRasterizer(pageCount: 3);
        var engine = new FakeOcrEngine(
            results: [("page one", 90f), ("page two", 80f), ("page three", 70f)]);
        var (sut, _, _) = MakeExtractor(rasterizer: rasterizer, engine: engine);
        await using (sut)
        {
            var result = await sut.ExtractAsync(new MemoryStream([1]), MediaTypes.Pdf);

            result.Pages.Should().HaveCount(3);
            result.Pages[0].PageNumber.Should().Be(1);
            result.Pages[0].Text.Should().Be("page one");
            result.Pages[1].PageNumber.Should().Be(2);
            result.Pages[1].Text.Should().Be("page two");
            result.Pages[2].PageNumber.Should().Be(3);
            result.Pages[2].Text.Should().Be("page three");
        }
    }

    /// <summary>Verifies that page text is joined with a single newline separator.</summary>
    [Fact]
    public async Task ExtractAsync_MultiPagePdf_CombinesTextWithNewlineAsync()
    {
        var rasterizer = new FakePdfRasterizer(pageCount: 2);
        var engine = new FakeOcrEngine(results: [("alpha", 90f), ("beta", 80f)]);
        var (sut, _, _) = MakeExtractor(rasterizer: rasterizer, engine: engine);
        await using (sut)
        {
            var result = await sut.ExtractAsync(new MemoryStream([1]), MediaTypes.Pdf);

            result.Text.Should().Be($"alpha{Environment.NewLine}beta");
        }
    }

    // -------------------------------------------------------------------------
    // Happy path: image media type skips rasterizer
    // -------------------------------------------------------------------------

    /// <summary>Verifies that PNG input bypasses the PDF rasterizer and produces a single-page result.</summary>
    [Fact]
    public async Task ExtractAsync_PngInput_SkipsRasterizerAsync()
    {
        var rasterizer = new FakePdfRasterizer(pageCount: 99); // page count irrelevant for images
        var engine = new FakeOcrEngine("image text", confidence: 75f);
        var (sut, _, _) = MakeExtractor(rasterizer: rasterizer, engine: engine);
        await using (sut)
        {
            var result = await sut.ExtractAsync(CreatePngStream(), MediaTypes.Png);

            result.Pages.Should().HaveCount(1);
            result.Text.Should().Be("image text");
        }
    }

    /// <summary>Verifies that JPEG input is accepted and produces a result.</summary>
    [Fact]
    public async Task ExtractAsync_JpegInput_SucceedsAsync()
    {
        var engine = new FakeOcrEngine("jpeg text", confidence: 70f);
        var (sut, _, _) = MakeExtractor(engine: engine);
        await using (sut)
        {
            var result = await sut.ExtractAsync(CreateJpegStream(), MediaTypes.Jpeg);

            result.Text.Should().Be("jpeg text");
        }
    }

    // -------------------------------------------------------------------------
    // Confidence aggregation
    // -------------------------------------------------------------------------

    /// <summary>Verifies that document mean confidence is the arithmetic mean of page confidences.</summary>
    [Fact]
    public async Task ExtractAsync_MultiPage_MeanConfidenceIsArithmeticMeanAsync()
    {
        var rasterizer = new FakePdfRasterizer(pageCount: 3);
        var engine = new FakeOcrEngine(
            results: [("a", 90f), ("b", 60f), ("c", 30f)]);
        var (sut, _, _) = MakeExtractor(rasterizer: rasterizer, engine: engine);
        await using (sut)
        {
            var result = await sut.ExtractAsync(new MemoryStream([1]), MediaTypes.Pdf);

            // Raw confidences 90, 60, 30 → normalized 0.9, 0.6, 0.3 → mean 0.6
            result.MeanConfidence.Should().BeApproximately(0.6, 1e-9);
        }
    }

    /// <summary>Verifies that page confidence values are normalized from the 0–100 raw scale to [0,1].</summary>
    [Fact]
    public async Task ExtractAsync_PageConfidence_NormalizedTo0To1Async()
    {
        var engine = new FakeOcrEngine("text", confidence: 85f);
        var (sut, _, _) = MakeExtractor(engine: engine);
        await using (sut)
        {
            var result = await sut.ExtractAsync(CreatePngStream(), MediaTypes.Png);

            result.Pages[0].Confidence.Should().BeApproximately(0.85, 1e-9);
        }
    }

    // -------------------------------------------------------------------------
    // Result metadata
    // -------------------------------------------------------------------------

    /// <summary>Verifies that result metadata reflects the configured options and engine version.</summary>
    [Fact]
    public async Task ExtractAsync_ReturnsCorrectMetadataAsync()
    {
        const string engineVersion = "5.3.0.test";
        var engine = new FakeOcrEngine("text", confidence: 80f, version: engineVersion);
        var (sut, _, _) = MakeExtractor(engine: engine, dpi: 200, languages: "fra");
        await using (sut)
        {
            var result = await sut.ExtractAsync(CreatePngStream(), MediaTypes.Png);

            result.EngineName.Should().Be("tesseract");
            result.EngineVersion.Should().Be(engineVersion);
            result.Language.Should().Be("fra");
            result.RasterizationDpi.Should().Be(200);
        }
    }

    // -------------------------------------------------------------------------
    // Lazy engine creation
    // -------------------------------------------------------------------------

    /// <summary>Verifies that the engine factory is not called at construction time.</summary>
    [Fact]
    public async Task ExtractAsync_EngineCreatedLazily_NotCalledAtConstructionAsync()
    {
        var (sut, factory, _) = MakeExtractor();
        await using (sut)
        {
            factory.CreateCount.Should().Be(0, "engine must not be created until first extraction");

            await sut.ExtractAsync(CreatePngStream(), MediaTypes.Png);

            factory.CreateCount.Should().Be(1, "engine must be created exactly once");
        }
    }

    /// <summary>Verifies that the engine is reused across multiple ExtractAsync calls.</summary>
    [Fact]
    public async Task ExtractAsync_CalledTwice_ReusesSameEngineAsync()
    {
        var (sut, factory, _) = MakeExtractor();
        await using (sut)
        {
            await sut.ExtractAsync(CreatePngStream(), MediaTypes.Png);
            await sut.ExtractAsync(CreatePngStream(), MediaTypes.Png);

            factory.CreateCount.Should().Be(1, "engine must be created only once across calls");
        }
    }

    // -------------------------------------------------------------------------
    // Native-load failure translation
    // -------------------------------------------------------------------------

    /// <summary>Verifies that DllNotFoundException from the factory is translated to NetIndexOcrNotInstalledException.</summary>
    [Fact]
    public async Task ExtractAsync_NativeLoadFails_ThrowsOcrNotInstalledExceptionAsync()
    {
        var factory = new FakeOcrEngineFactory(throwOnCreate: new DllNotFoundException("libtesseract.so"));
        var (sut, _, _) = MakeExtractor(factory: factory);
        await using (sut)
        {
            var act = async () => await sut.ExtractAsync(CreatePngStream(), MediaTypes.Png);

            await act.Should().ThrowAsync<NetIndexOcrNotInstalledException>();
        }
    }

    // -------------------------------------------------------------------------
    // Disposal
    // -------------------------------------------------------------------------

    /// <summary>Verifies that DisposeAsync can be called multiple times without error.</summary>
    [Fact]
    public async Task DisposeAsync_CalledTwice_IsIdempotentAsync()
    {
        var (sut, _, _) = MakeExtractor();

        await sut.DisposeAsync();
        var act = async () => await sut.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    /// <summary>Verifies that ExtractAsync throws ObjectDisposedException after disposal.</summary>
    [Fact]
    public async Task ExtractAsync_AfterDispose_ThrowsObjectDisposedExceptionAsync()
    {
        var (sut, _, _) = MakeExtractor();
        await sut.DisposeAsync();

        var act = async () => await sut.ExtractAsync(CreatePngStream(), MediaTypes.Png);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    // -------------------------------------------------------------------------
    // Serialized concurrency
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that concurrent ExtractAsync calls do not interfere: each produces the correct result.
    /// The SemaphoreSlim(1,1) serializes OCR but all callers still receive valid responses.
    /// </summary>
    [Fact]
    public async Task ExtractAsync_ConcurrentCalls_AllSucceedAsync()
    {
        const int concurrency = 5;
        var engine = new FakeOcrEngine("concurrent text", confidence: 90f);
        var (sut, _, _) = MakeExtractor(engine: engine);
        await using (sut)
        {
            var tasks = Enumerable.Range(0, concurrency)
                .Select(_ => sut.ExtractAsync(CreatePngStream(), MediaTypes.Png))
                .ToList();

            var results = await Task.WhenAll(tasks);

            results.Should().HaveCount(concurrency);
            results.Should().OnlyContain(r => r.Text == "concurrent text");
        }
    }

    // -------------------------------------------------------------------------
    /// <summary>Verifies that image dimensions are bounded before native image decoding.</summary>
    [Fact]
    public async Task ExtractAsync_ImagePixelLimitExceeded_DoesNotInvokeEngineAsync()
    {
        var engine = new FakeOcrEngine();
        var (sut, _, _) = MakeExtractor(engine: engine, maxPixelsPerPage: 100);
        await using (sut)
        {
            var act = async () => await sut.ExtractAsync(CreatePngStream(20, 20), MediaTypes.Png);

            var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
            ex.Which.ErrorCode.Should().Be("pixel_limit_exceeded");
            engine.RecognizeCount.Should().Be(0);
        }
    }

    /// <summary>Verifies that page-count failures are translated to render_failed.</summary>
    [Fact]
    public async Task ExtractAsync_PageCountFails_ThrowsProviderExceptionAsync()
    {
        var rasterizer = new FakePdfRasterizer();
        rasterizer.ThrowOnPageCount(new InvalidDataException("corrupt PDF"));
        var (sut, _, _) = MakeExtractor(rasterizer: rasterizer);
        await using (sut)
        {
            var act = async () => await sut.ExtractAsync(new MemoryStream([1]), MediaTypes.Pdf);

            var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
            ex.Which.ErrorCode.Should().Be("render_failed");
            ex.Which.InnerException.Should().BeOfType<InvalidDataException>();
        }
    }

    /// <summary>Verifies that a rendered stream is disposed when cancellation wins before recognition.</summary>
    [Fact]
    public async Task ExtractAsync_CancelledAfterRasterization_DisposesRenderedStreamAsync()
    {
        using var cts = new CancellationTokenSource();
        var rasterizer = new FakePdfRasterizer(onRasterize: () => _ = cts.CancelAsync());
        var (sut, _, _) = MakeExtractor(rasterizer: rasterizer);
        await using (sut)
        {
            var act = async () => await sut.ExtractAsync(new MemoryStream([1]), MediaTypes.Pdf, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            rasterizer.LastRenderedStream.Should().NotBeNull();
            rasterizer.LastRenderedStream!.IsDisposed.Should().BeTrue();
        }
    }

    /// <summary>Verifies that general engine initialization failures become configuration errors.</summary>
    [Fact]
    public async Task ExtractAsync_EngineInitializationFails_ThrowsConfigurationExceptionAsync()
    {
        var factory = new FakeOcrEngineFactory(throwOnCreate: new InvalidOperationException("bad trained data"));
        var (sut, _, _) = MakeExtractor(factory: factory);
        await using (sut)
        {
            var act = async () => await sut.ExtractAsync(CreatePngStream(), MediaTypes.Png);

            var ex = await act.Should().ThrowAsync<NetIndexConfigurationException>();
            ex.Which.InnerException.Should().BeOfType<InvalidOperationException>();
        }
    }

    /// <summary>Verifies that native-load failures during recognition retain install guidance.</summary>
    [Fact]
    public async Task ExtractAsync_NativeLoadFailsDuringRecognition_ThrowsOcrNotInstalledAsync()
    {
        var engine = new FakeOcrEngine();
        engine.ThrowOnRecognize(new DllNotFoundException("libleptonica"));
        var (sut, _, _) = MakeExtractor(engine: engine);
        await using (sut)
        {
            var act = async () => await sut.ExtractAsync(CreatePngStream(), MediaTypes.Png);

            var ex = await act.Should().ThrowAsync<NetIndexOcrNotInstalledException>();
            ex.Which.RequiredPackage.Should().Be("NetIndex.Ingestion.Tesseract");
        }
    }

    /// <summary>Verifies that non-finite confidence values are rejected.</summary>
    [Fact]
    public async Task ExtractAsync_NonFiniteConfidence_ThrowsRecognitionFailureAsync()
    {
        var engine = new FakeOcrEngine("text", float.NaN);
        var (sut, _, _) = MakeExtractor(engine: engine);
        await using (sut)
        {
            var act = async () => await sut.ExtractAsync(CreatePngStream(), MediaTypes.Png);

            var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
            ex.Which.ErrorCode.Should().Be("recognition_failed");
        }
    }

    /// <summary>Verifies that finite confidence is bounded to the public contract range.</summary>
    [Theory]
    [InlineData(-10f, 0.0)]
    [InlineData(150f, 1.0)]
    public async Task ExtractAsync_OutOfRangeConfidence_IsClampedAsync(float raw, double expected)
    {
        var engine = new FakeOcrEngine("text", raw);
        var (sut, _, _) = MakeExtractor(engine: engine);
        await using (sut)
        {
            var result = await sut.ExtractAsync(CreatePngStream(), MediaTypes.Png);

            result.Pages[0].Confidence.Should().Be(expected);
        }
    }

    /// <summary>Verifies that disposal waits for active extraction and disposes the engine once.</summary>
    [Fact]
    public async Task DisposeAsync_DuringExtraction_WaitsForActiveCallAsync()
    {
        var engine = new BlockingOcrEngine();
        var factory = new FakeOcrEngineFactory(engine: engine);
        var (sut, _, _) = MakeExtractor(factory: factory);

        var extraction = sut.ExtractAsync(CreatePngStream(), MediaTypes.Png);
        await engine.Started.WaitAsync(TimeSpan.FromSeconds(5));
        var disposal = sut.DisposeAsync().AsTask();

        disposal.IsCompleted.Should().BeFalse();
        engine.Release();
        await extraction;
        await disposal;
        engine.DisposeCount.Should().Be(1);
    }

    private static MemoryStream CreatePngStream(uint width = 1, uint height = 1)
    {
        byte[] bytes =
        [
            137, 80, 78, 71, 13, 10, 26, 10,
            0, 0, 0, 13, 73, 72, 68, 82,
            (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
            (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
        ];
        return new MemoryStream(bytes);
    }

    private static MemoryStream CreateJpegStream(ushort width = 1, ushort height = 1)
    {
        byte[] bytes =
        [
            0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x11, 0x08,
            (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width,
            0x03, 0x01, 0x11, 0x00, 0x02, 0x11, 0x00, 0x03, 0x11, 0x00,
            0xFF, 0xD9,
        ];
        return new MemoryStream(bytes);
    }

    // Factories and fakes
    // -------------------------------------------------------------------------

    private static (TesseractVisionExtractor extractor, FakeOcrEngineFactory factory, FakeOcrEngine engine) MakeExtractor(
        FakePdfRasterizer? rasterizer = null,
        FakeOcrEngine? engine = null,
        FakeOcrEngineFactory? factory = null,
        int maxInputBytes = 52_428_800,
        int maxPages = 100,
        long maxPixelsPerPage = 50_000_000,
        int dpi = 300,
        string languages = "eng")
    {
        var fakeEngine = engine ?? new FakeOcrEngine("test text", confidence: 80f);
        var fakeFactory = factory ?? new FakeOcrEngineFactory(engine: fakeEngine);
        var fakeRasterizer = rasterizer ?? new FakePdfRasterizer(pageCount: 1);

        var options = Microsoft.Extensions.Options.Options.Create(new TesseractOptions
        {
            TessDataPath = "/fake",
            Languages = languages,
            RasterizationDpi = dpi,
            MaxInputBytes = maxInputBytes,
            MaxPages = maxPages,
            MaxPixelsPerPage = maxPixelsPerPage,
        });

        var extractor = new TesseractVisionExtractor(options, fakeRasterizer, fakeFactory);
        return (extractor, fakeFactory, fakeEngine);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Fake adapters — accessible via InternalsVisibleTo
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Fake PDF rasterizer with configurable bounds, callbacks, and failures.</summary>


/// <summary>
/// Fake OCR engine with configurable per-call results, optional recognition failure, and optional cancellation trigger.
/// </summary>
internal sealed class FakeOcrEngine : IOcrEngine
{
    private readonly Queue<(string text, float confidence)> _results;
    private readonly string _defaultText;
    private readonly float _defaultConfidence;
    private readonly string _version;
    private readonly Action? _onRecognize;
    private Exception? _throwOnRecognize;
    private int _recognizeCount;
    private int _disposeCount;

    internal FakeOcrEngine(
        string defaultText = "default text",
        float confidence = 80f,
        string version = "test-1.0",
        Action? onRecognize = null,
        IEnumerable<(string text, float confidence)>? results = null)
    {
        _defaultText = defaultText;
        _defaultConfidence = confidence;
        _version = version;
        _onRecognize = onRecognize;
        _results = results is not null
            ? new Queue<(string, float)>(results)
            : new Queue<(string, float)>();
    }

    public string Version => _version;
    internal int RecognizeCount => _recognizeCount;
    internal int DisposeCount => _disposeCount;

    internal void ThrowOnRecognize(Exception ex) => _throwOnRecognize = ex;

    public Task<(string text, float confidenceRaw)> RecognizeAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _recognizeCount);
        _onRecognize?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();

        if (_throwOnRecognize is not null)
        {
            throw _throwOnRecognize;
        }

        var (text, conf) = _results.Count > 0
            ? _results.Dequeue()
            : (_defaultText, _defaultConfidence);

        return Task.FromResult((text, conf));
    }

    public void Dispose() => Interlocked.Increment(ref _disposeCount);
}

internal sealed class BlockingOcrEngine : IOcrEngine
{
    private readonly SemaphoreSlim _started = new(0, 1);
    private readonly SemaphoreSlim _release = new(0, 1);
    private int _disposeCount;

    public string Version => "blocking-test";
    internal SemaphoreSlim Started => _started;
    internal int DisposeCount => _disposeCount;
    internal void Release() => _release.Release();

    public async Task<(string text, float confidenceRaw)> RecognizeAsync(
        Stream imageStream,
        CancellationToken cancellationToken = default)
    {
        _started.Release();
        await _release.WaitAsync(cancellationToken).ConfigureAwait(false);
        return ("completed", 80f);
    }

    public void Dispose()
    {
        Interlocked.Increment(ref _disposeCount);
        _started.Dispose();
        _release.Dispose();
    }
}

/// <summary>Fake OCR engine factory that either returns a configured engine or throws on create.</summary>
internal sealed class FakeOcrEngineFactory : IOcrEngineFactory
{
    private readonly IOcrEngine? _engine;
    private readonly Exception? _throwOnCreate;
    private int _createCount;

    internal FakeOcrEngineFactory(IOcrEngine? engine = null, Exception? throwOnCreate = null)
    {
        _engine = engine;
        _throwOnCreate = throwOnCreate;
    }

    internal int CreateCount => _createCount;

    public IOcrEngine Create()
    {
        Interlocked.Increment(ref _createCount);

        if (_throwOnCreate is not null)
        {
            throw _throwOnCreate;
        }

        return _engine ?? throw new InvalidOperationException("No engine configured.");
    }
}
