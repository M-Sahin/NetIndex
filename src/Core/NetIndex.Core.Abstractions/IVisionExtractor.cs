using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NetIndex.Core.Abstractions;

/// <summary>
/// Extracts text from image-bearing documents or images using an OCR engine.
/// </summary>
/// <remarks>
/// <para>
/// Contract-level media-type support (checked by <see cref="ExtractAsync"/>):
/// <list type="bullet">
///   <item><see cref="MediaTypes.Pdf"/> — scanned PDF pages are rasterized then recognized.</item>
///   <item><see cref="MediaTypes.Png"/> and <see cref="MediaTypes.Jpeg"/> — image recognized directly.</item>
/// </list>
/// Unsupported media types throw <see cref="NetIndexProviderException"/> with
/// <c>ErrorCode = "unsupported_media_type"</c>.
/// </para>
/// <para>No Tesseract, PDFium, or SkiaSharp types appear in any public signature of this contract.</para>
/// </remarks>
public interface IVisionExtractor
{
    /// <summary>
    /// Extracts text from <paramref name="source"/> using OCR.
    /// </summary>
    /// <param name="source">Readable stream of the document or image.</param>
    /// <param name="mediaType">
    /// MIME type of the source — use <see cref="MediaTypes"/> constants.
    /// Unsupported values throw <see cref="NetIndexProviderException"/> with <c>ErrorCode = "unsupported_media_type"</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Combined text, document mean confidence, and ordered per-page results.
    /// </returns>
    /// <exception cref="NetIndexProviderException">Thrown for unsupported media types, render/recognition failures, and all-whitespace output.</exception>
    /// <exception cref="NetIndexOcrNotInstalledException">Thrown when the native OCR library cannot be loaded.</exception>
    Task<VisionExtractionResult> ExtractAsync(
        Stream source,
        string mediaType,
        CancellationToken cancellationToken = default);
}
