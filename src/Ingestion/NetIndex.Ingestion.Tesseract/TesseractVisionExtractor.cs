using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Tesseract.Internal;
using NetIndex.Ingestion.Tesseract.Options;

namespace NetIndex.Ingestion.Tesseract;

/// <summary>
/// <see cref="IVisionExtractor"/> implementation backed by TesseractOCR and PDFtoImage.
/// </summary>
public sealed class TesseractVisionExtractor : IVisionExtractor, IAsyncDisposable
{
    private static readonly HashSet<string> SupportedMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        MediaTypes.Pdf,
        MediaTypes.Png,
        MediaTypes.Jpeg,
    };

    private readonly TesseractOptions _options;
    private readonly IPdfPageRasterizer _rasterizer;
    private readonly IOcrEngineFactory _engineFactory;
    private readonly SemaphoreSlim _engineLock = new(1, 1);
    private readonly object _lifecycleSync = new();
    private IOcrEngine? _engine;
    private TaskCompletionSource? _activeCallsDrained;
    private Task? _disposeTask;
    private int _activeCalls;
    private int _disposeState;

    /// <summary>
    /// Production constructor resolved by dependency injection. Native loading remains lazy.
    /// </summary>
    public TesseractVisionExtractor(IOptions<TesseractOptions> options)
        : this(
            options,
            new PdfToImageRasterizer(),
            new TesseractOcrEngineFactory(options.Value.TessDataPath, options.Value.Languages))
    { }

    internal TesseractVisionExtractor(
        IOptions<TesseractOptions> options,
        IPdfPageRasterizer rasterizer,
        IOcrEngineFactory engineFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(rasterizer);
        ArgumentNullException.ThrowIfNull(engineFactory);
        _options = options.Value;
        _rasterizer = rasterizer;
        _engineFactory = engineFactory;
    }

    /// <inheritdoc />
    public async Task<VisionExtractionResult> ExtractAsync(
        Stream source,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        EnterCall();
        try
        {
            if (!SupportedMediaTypes.Contains(mediaType))
            {
                throw ProviderError(
                    $"Unsupported media type '{mediaType}'. Supported: {string.Join(", ", SupportedMediaTypes)}.",
                    "unsupported_media_type");
            }

            using var bufferedInput = await BufferInputAsync(source, cancellationToken).ConfigureAwait(false);
            var isPdf = string.Equals(mediaType, MediaTypes.Pdf, StringComparison.OrdinalIgnoreCase);
            var pageCount = isPdf
                ? await GetPageCountAsync(bufferedInput, cancellationToken).ConfigureAwait(false)
                : 1;

            if (pageCount > _options.MaxPages)
            {
                throw ProviderError(
                    $"PDF has {pageCount} pages which exceeds the configured limit of {_options.MaxPages}.",
                    "page_limit_exceeded");
            }

            var pageResults = new List<VisionPageResult>(pageCount);
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pixels = isPdf
                    ? await GetPdfPixelCountAsync(bufferedInput, pageIndex, cancellationToken).ConfigureAwait(false)
                    : GetImagePixelCount(bufferedInput, mediaType);
                ThrowIfPixelLimitExceeded(pixels, pageIndex + 1);

                Stream imageStream;
                var ownsImageStream = isPdf;
                if (isPdf)
                {
                    imageStream = await RasterizePageAsync(bufferedInput, pageIndex, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    bufferedInput.Position = 0;
                    imageStream = bufferedInput;
                }

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (pageText, confidenceRaw) = await RecognizePageAsync(
                        imageStream,
                        pageIndex + 1,
                        cancellationToken).ConfigureAwait(false);

                    if (!float.IsFinite(confidenceRaw))
                    {
                        throw ProviderError(
                            $"OCR returned a non-finite confidence for page {pageIndex + 1}.",
                            "recognition_failed");
                    }

                    pageResults.Add(new VisionPageResult(
                        PageNumber: pageIndex + 1,
                        Text: pageText,
                        Confidence: Math.Clamp(confidenceRaw / 100.0, 0.0, 1.0)));
                }
                finally
                {
                    if (ownsImageStream)
                    {
                        await imageStream.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }

            var combinedText = CombineText(pageResults);
            if (string.IsNullOrWhiteSpace(combinedText))
            {
                throw ProviderError(
                    "OCR produced no recognizable text (all-whitespace output).",
                    "empty_ocr_result");
            }

            var meanConfidence = pageResults.Count > 0
                ? SumConfidences(pageResults) / pageResults.Count
                : 0.0;

            return new VisionExtractionResult(
                Text: combinedText,
                MeanConfidence: meanConfidence,
                Pages: pageResults,
                EngineName: "tesseract",
                EngineVersion: _engine?.Version ?? "unknown",
                Language: _options.Languages,
                RasterizationDpi: _options.RasterizationDpi);
        }
        finally
        {
            ExitCall();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_lifecycleSync)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            _disposeState = 1;
            var drained = _activeCalls == 0
                ? Task.CompletedTask
                : (_activeCallsDrained = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            _disposeTask = DisposeCoreAsync(drained);
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync(Task activeCallsDrained)
    {
#pragma warning disable VSTHRD003 // Task is created and completed by this instance's lifecycle gate.
        await activeCallsDrained.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        await _engineLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _engine?.Dispose();
            _engine = null;
        }
        finally
        {
            _engineLock.Release();
            _engineLock.Dispose();
        }
    }

    private async Task<int> GetPageCountAsync(MemoryStream source, CancellationToken cancellationToken)
    {
        try
        {
            source.Position = 0;
            return await _rasterizer.GetPageCountAsync(source, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsNativeLoadFailure(ex))
        {
            throw NativeUnavailable(ex);
        }
        catch (Exception ex)
        {
            throw ProviderError("Failed to read PDF page count.", "render_failed", ex);
        }
    }

    private async Task<long> GetPdfPixelCountAsync(
        MemoryStream source,
        int pageIndex,
        CancellationToken cancellationToken)
    {
        try
        {
            source.Position = 0;
            return await _rasterizer
                .GetPagePixelCountAsync(source, pageIndex, _options.RasterizationDpi, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsNativeLoadFailure(ex))
        {
            throw NativeUnavailable(ex);
        }
        catch (Exception ex)
        {
            throw ProviderError($"Failed to inspect page {pageIndex + 1}.", "render_failed", ex);
        }
    }

    private async Task<Stream> RasterizePageAsync(
        MemoryStream source,
        int pageIndex,
        CancellationToken cancellationToken)
    {
        try
        {
            source.Position = 0;
            return await _rasterizer
                .RasterizePageAsync(source, pageIndex, _options.RasterizationDpi, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsNativeLoadFailure(ex))
        {
            throw NativeUnavailable(ex);
        }
        catch (Exception ex)
        {
            throw ProviderError($"Failed to rasterize page {pageIndex + 1}.", "render_failed", ex);
        }
    }

    private async Task<(string text, float confidenceRaw)> RecognizePageAsync(
        Stream imageStream,
        int pageNumber,
        CancellationToken cancellationToken)
    {
        await _engineLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _engine ??= CreateEngineGuarded();
            try
            {
                return await _engine.RecognizeAsync(imageStream, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsNativeLoadFailure(ex))
            {
                throw NativeUnavailable(ex);
            }
            catch (NetIndexException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw ProviderError($"OCR recognition failed on page {pageNumber}.", "recognition_failed", ex);
            }
        }
        finally
        {
            _engineLock.Release();
        }
    }

    private IOcrEngine CreateEngineGuarded()
    {
        try
        {
            return _engineFactory.Create();
        }
        catch (NetIndexOcrNotInstalledException)
        {
            throw;
        }
        catch (Exception ex) when (IsNativeLoadFailure(ex))
        {
            throw NativeUnavailable(ex);
        }
        catch (Exception ex)
        {
            throw new NetIndexConfigurationException(
                "Tesseract engine initialization failed. Verify TessDataPath and trained-data files.",
                nameof(TesseractOptions.TessDataPath),
                "Valid Tesseract trained data",
                _options.TessDataPath,
                ex);
        }
    }

    private async Task<MemoryStream> BufferInputAsync(Stream source, CancellationToken cancellationToken)
    {
        var output = new MemoryStream();
        var buffer = new byte[81920];
        long totalRead = 0;
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                totalRead += read;
                if (totalRead > _options.MaxInputBytes)
                {
                    throw ProviderError(
                        $"Input stream exceeds the configured limit of {_options.MaxInputBytes:N0} bytes.",
                        "input_too_large");
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            output.Position = 0;
            return output;
        }
        catch
        {
            await output.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private long GetImagePixelCount(MemoryStream source, string mediaType)
    {
        try
        {
            if (!source.TryGetBuffer(out var buffer))
            {
                throw new InvalidDataException("Buffered image bytes are unavailable.");
            }

            var bytes = buffer.AsSpan(0, checked((int)source.Length));
            return string.Equals(mediaType, MediaTypes.Png, StringComparison.OrdinalIgnoreCase)
                ? ReadPngPixelCount(bytes)
                : ReadJpegPixelCount(bytes);
        }
        catch (Exception ex) when (ex is not NetIndexException)
        {
            throw ProviderError("Failed to inspect image dimensions.", "render_failed", ex);
        }
    }

    private static long ReadPngPixelCount(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 24 || !bytes[..8].SequenceEqual(signature))
        {
            throw new InvalidDataException("Input is not a valid PNG image.");
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4));
        return checked((long)width * height);
    }

    private static long ReadJpegPixelCount(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
        {
            throw new InvalidDataException("Input is not a valid JPEG image.");
        }

        var offset = 2;
        while (offset < bytes.Length)
        {
            while (offset < bytes.Length && bytes[offset] == 0xFF)
            {
                offset++;
            }
            if (offset >= bytes.Length)
            {
                break;
            }

            var marker = bytes[offset++];
            if (marker is 0xD8 or 0xD9 || marker is >= 0xD0 and <= 0xD7 || marker == 0x01)
            {
                continue;
            }
            if (marker == 0xDA || offset + 2 > bytes.Length)
            {
                break;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            if (segmentLength < 2 || offset + segmentLength > bytes.Length)
            {
                throw new InvalidDataException("JPEG segment length is invalid.");
            }

            if (IsStartOfFrame(marker))
            {
                if (segmentLength < 7)
                {
                    throw new InvalidDataException("JPEG frame header is invalid.");
                }
                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2));
                return checked((long)width * height);
            }

            offset += segmentLength;
        }

        throw new InvalidDataException("JPEG dimensions were not found.");
    }

    private static bool IsStartOfFrame(byte marker)
        => marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or
            0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;

    private void ThrowIfPixelLimitExceeded(long pixels, int pageNumber)
    {
        if (pixels > _options.MaxPixelsPerPage)
        {
            throw ProviderError(
                $"Page {pageNumber} has {pixels:N0} pixels which exceeds the configured limit of {_options.MaxPixelsPerPage:N0}.",
                "pixel_limit_exceeded");
        }
    }

    private void EnterCall()
    {
        lock (_lifecycleSync)
        {
            if (_disposeState != 0)
            {
                throw new ObjectDisposedException(nameof(TesseractVisionExtractor));
            }
            _activeCalls++;
        }
    }

    private void ExitCall()
    {
        lock (_lifecycleSync)
        {
            _activeCalls--;
            if (_activeCalls == 0)
            {
                _activeCallsDrained?.TrySetResult();
            }
        }
    }

    private static bool IsNativeLoadFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DllNotFoundException or EntryPointNotFoundException or
                BadImageFormatException or TypeInitializationException or
                FileNotFoundException or FileLoadException)
            {
                return true;
            }
        }
        return false;
    }

    private static NetIndexOcrNotInstalledException NativeUnavailable(Exception exception)
    {
        var instructions = OperatingSystem.IsWindows()
            ? "Install the Visual C++ 2022 Redistributable and ensure the wrapper DLLs are present."
            : "Install Tesseract and Leptonica and provision the wrapper-required loader aliases.";
        return new NetIndexOcrNotInstalledException(
            $"Tesseract native dependencies could not be loaded: {exception.Message}",
            requiredPackage: "NetIndex.Ingestion.Tesseract",
            installInstructions: instructions,
            innerException: exception);
    }

    private static NetIndexProviderException ProviderError(
        string message,
        string errorCode,
        Exception? innerException = null)
        => new(
            message,
            isRetryable: false,
            providerName: "Tesseract",
            errorCode: errorCode,
            httpStatusCode: null,
            innerException: innerException);

    private static string CombineText(List<VisionPageResult> pages)
    {
        var output = new StringBuilder();
        for (var i = 0; i < pages.Count; i++)
        {
            if (i > 0)
            {
                output.AppendLine();
            }
            output.Append(pages[i].Text);
        }
        return output.ToString();
    }

    private static double SumConfidences(List<VisionPageResult> pages)
    {
        double sum = 0;
        foreach (var page in pages)
        {
            sum += page.Confidence;
        }
        return sum;
    }
}
