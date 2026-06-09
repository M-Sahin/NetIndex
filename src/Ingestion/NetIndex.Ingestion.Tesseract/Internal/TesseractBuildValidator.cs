using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Tesseract.Options;

namespace NetIndex.Ingestion.Tesseract.Internal;

/// <summary>
/// Forces managed Tesseract option validation during <see cref="INetIndexBuilder.Build"/>.
/// </summary>
internal sealed class TesseractBuildValidator(IOptions<TesseractOptions> options) : INetIndexBuildValidator
{
    public void Validate() => _ = options.Value;
}
