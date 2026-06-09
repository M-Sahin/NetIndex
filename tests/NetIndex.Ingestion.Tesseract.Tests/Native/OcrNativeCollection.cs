using NetIndex.Testing.Common;
using Xunit;

namespace NetIndex.Ingestion.Tesseract.Tests.Native;

/// <summary>
/// xUnit collection definition for native Tesseract OCR tests.
/// Parallelization is disabled so that tests sharing the Tesseract engine do not race.
/// Apply <c>[Collection(TestingConstants.Collections.Tesseract)]</c> to each native test class.
/// </summary>
[CollectionDefinition(TestingConstants.Collections.Tesseract, DisableParallelization = true)]
public sealed class OcrNativeCollection
{
}
