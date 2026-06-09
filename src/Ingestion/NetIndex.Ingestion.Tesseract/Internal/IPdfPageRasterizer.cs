using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NetIndex.Ingestion.Tesseract.Internal;

/// <summary>
/// Rasterizes a single page from a PDF stream to a PNG image stream.
/// </summary>
internal interface IPdfPageRasterizer
{
    /// <summary>Returns the number of pages in the given PDF stream.</summary>
    Task<int> GetPageCountAsync(Stream pdfStream, CancellationToken cancellationToken = default);

    /// <summary>Calculates the rendered pixel count without allocating the rendered bitmap.</summary>
    Task<long> GetPagePixelCountAsync(
        Stream pdfStream,
        int pageIndex,
        int dpi,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rasterizes the page at the given zero-based index to a PNG stream owned by the caller.
    /// </summary>
    Task<Stream> RasterizePageAsync(
        Stream pdfStream,
        int pageIndex,
        int dpi,
        CancellationToken cancellationToken = default);
}
