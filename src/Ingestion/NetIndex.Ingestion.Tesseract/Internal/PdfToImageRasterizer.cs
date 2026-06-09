using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PDFtoImage;

namespace NetIndex.Ingestion.Tesseract.Internal;

/// <summary>
/// Rasterizes PDF pages to PNG images using PDFtoImage (PDFium).
/// PDFium serializes concurrent access internally; do not add page parallelism.
/// </summary>
#pragma warning disable CA1416
internal sealed class PdfToImageRasterizer : IPdfPageRasterizer
{
    public Task<int> GetPageCountAsync(Stream pdfStream, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        pdfStream.Position = 0;
        return Task.FromResult(Conversion.GetPageCount(pdfStream, leaveOpen: true));
    }

    public Task<long> GetPagePixelCountAsync(
        Stream pdfStream,
        int pageIndex,
        int dpi,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        pdfStream.Position = 0;
        var size = Conversion.GetPageSize(pdfStream, pageIndex, leaveOpen: true);
        var width = checked((long)Math.Ceiling(size.Width * dpi / 72.0));
        var height = checked((long)Math.Ceiling(size.Height * dpi / 72.0));
        return Task.FromResult(checked(width * height));
    }

    public async Task<Stream> RasterizePageAsync(
        Stream pdfStream,
        int pageIndex,
        int dpi,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        pdfStream.Position = 0;
        var output = new MemoryStream();
        try
        {
            Conversion.SavePng(
                output,
                pdfStream,
                pageIndex,
                leaveOpen: true,
                options: new RenderOptions(Dpi: dpi));
            output.Position = 0;
            return output;
        }
        catch
        {
            await output.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
#pragma warning restore CA1416
