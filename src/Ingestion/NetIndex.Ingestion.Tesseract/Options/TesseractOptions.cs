namespace NetIndex.Ingestion.Tesseract.Options;

/// <summary>
/// Configuration options for <see cref="TesseractVisionExtractor"/>.
/// </summary>
public sealed class TesseractOptions
{
    /// <summary>
    /// Path to the directory containing Tesseract <c>.traineddata</c> language files (required).
    /// </summary>
    public string TessDataPath { get; set; } = string.Empty;

    /// <summary>
    /// Tesseract language code(s) to use for recognition (default: <c>"eng"</c>).
    /// Multiple languages can be combined with a <c>+</c> separator (e.g., <c>"eng+fra"</c>).
    /// </summary>
    public string Languages { get; set; } = "eng";

    /// <summary>
    /// DPI at which PDF pages are rasterized before OCR (default: <c>300</c>; valid range: 72–600).
    /// </summary>
    public int RasterizationDpi { get; set; } = 300;

    /// <summary>
    /// Maximum number of bytes accepted from the input stream (default: 50 MB).
    /// Must be positive.
    /// </summary>
    public long MaxInputBytes { get; set; } = 52_428_800;

    /// <summary>
    /// Maximum number of pages to process from a single document (default: <c>100</c>).
    /// Must be positive.
    /// </summary>
    public int MaxPages { get; set; } = 100;

    /// <summary>
    /// Maximum pixels per page (width × height) before the page is rejected (default: 50,000,000).
    /// Must be positive.
    /// </summary>
    public long MaxPixelsPerPage { get; set; } = 50_000_000;
}
