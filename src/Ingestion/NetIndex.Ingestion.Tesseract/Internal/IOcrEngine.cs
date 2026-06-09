using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NetIndex.Ingestion.Tesseract.Internal;

/// <summary>
/// Recognizes text in a rasterized image.
/// Implementations wrap the native Tesseract OCR engine.
/// </summary>
internal interface IOcrEngine : IDisposable
{
    /// <summary>Version string of the underlying OCR engine.</summary>
    string Version { get; }

    /// <summary>
    /// Recognizes text in the given PNG image stream.
    /// </summary>
    /// <param name="imageStream">PNG image stream (will be read from current position).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recognized text and raw confidence (0–100 scale).</returns>
    Task<(string text, float confidenceRaw)> RecognizeAsync(Stream imageStream, CancellationToken cancellationToken = default);
}
