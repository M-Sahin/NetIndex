namespace NetIndex.Ingestion.Pdf.Options;

/// <summary>
/// Configuration options for <see cref="Loaders.PdfDocumentLoader"/>.
/// </summary>
public sealed class PdfDocumentLoaderOptions
{
    /// <summary>
    /// Minimum average number of characters per page required to consider a PDF as text-based.
    /// PDFs falling below this threshold are treated as scanned images and will cause
    /// <see cref="NetIndex.Core.Abstractions.NetIndexOcrNotInstalledException"/> to be thrown.
    /// </summary>
    public int MinimumTextPerPageThreshold { get; set; } = 10;
}
