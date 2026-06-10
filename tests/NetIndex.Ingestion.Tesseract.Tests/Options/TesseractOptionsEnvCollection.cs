using NetIndex.Testing.Common;
using Xunit;

namespace NetIndex.Ingestion.Tesseract.Tests.Options;

/// <summary>
/// xUnit collection definition for managed Tesseract tests that mutate the process-global
/// <c>TESSDATA_PREFIX</c> environment variable. Parallelization is disabled so the env var
/// cannot leak into other tests (e.g. those resolving <c>IOptions&lt;TesseractOptions&gt;</c>)
/// that run concurrently. Apply <c>[Collection(TestingConstants.Collections.TesseractOptionsEnv)]</c>
/// to each such test class.
/// </summary>
[CollectionDefinition(TestingConstants.Collections.TesseractOptionsEnv, DisableParallelization = true)]
public sealed class TesseractOptionsEnvCollection
{
}
