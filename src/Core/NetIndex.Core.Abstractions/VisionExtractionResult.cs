using System.Collections.Generic;

namespace NetIndex.Core.Abstractions;

/// <summary>
/// Combined OCR result returned by <see cref="IVisionExtractor"/>.
/// Contains the primary extraction output (text, confidence, pages) plus engine metadata
/// that callers can surface as document metadata.
/// </summary>
/// <param name="Text">All recognized text, pages joined in source order.</param>
/// <param name="MeanConfidence">Arithmetic mean of per-page confidences, normalized to [0,1].</param>
/// <param name="Pages">Per-page results in source order.</param>
/// <param name="EngineName">Short name of the OCR engine (e.g., <c>"tesseract"</c>).</param>
/// <param name="EngineVersion">Version string of the OCR engine.</param>
/// <param name="Language">Language(s) used for recognition.</param>
/// <param name="RasterizationDpi">DPI at which source pages were rasterized before recognition.</param>
public sealed record VisionExtractionResult(
    string Text,
    double MeanConfidence,
    IReadOnlyList<VisionPageResult> Pages,
    string EngineName,
    string EngineVersion,
    string Language,
    int RasterizationDpi);
