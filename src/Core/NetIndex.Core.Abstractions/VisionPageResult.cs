namespace NetIndex.Core.Abstractions;

/// <summary>
/// OCR result for a single page.
/// </summary>
/// <param name="PageNumber">One-based page number.</param>
/// <param name="Text">Recognized text for this page.</param>
/// <param name="Confidence">Recognition confidence normalized to [0,1].</param>
public sealed record VisionPageResult(int PageNumber, string Text, double Confidence);
