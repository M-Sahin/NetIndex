using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TesseractOCR;
using TesseractOCR.Pix;

namespace NetIndex.Ingestion.Tesseract.Internal;

/// <summary>
/// OCR engine backed by the TesseractOCR native library.
/// </summary>
internal sealed class TesseractOcrEngine : IOcrEngine
{
    private readonly Engine _engine;

    internal TesseractOcrEngine(Engine engine)
    {
        _engine = engine;
    }

    public string Version => _engine.Version ?? "unknown";

    public async Task<(string text, float confidenceRaw)> RecognizeAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var imageBytes = await ReadAllBytesAsync(imageStream, cancellationToken).ConfigureAwait(false);
        using var pix = Image.LoadFromMemory(imageBytes);
        using var page = _engine.Process(pix);
        cancellationToken.ThrowIfCancellationRequested();

        var text = page.Text ?? string.Empty;
        var confidence = page.MeanConfidence;
        return (text, confidence);
    }

    public void Dispose() => _engine.Dispose();

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream is MemoryStream ms)
        {
            return ms.ToArray();
        }
        using var buf = new MemoryStream();
        await stream.CopyToAsync(buf, cancellationToken).ConfigureAwait(false);
        return buf.ToArray();
    }
}
