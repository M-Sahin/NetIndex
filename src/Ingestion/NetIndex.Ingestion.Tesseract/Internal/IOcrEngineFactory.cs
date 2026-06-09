namespace NetIndex.Ingestion.Tesseract.Internal;

/// <summary>
/// Creates <see cref="IOcrEngine"/> instances.
/// Exists as a seam for replacing the native Tesseract engine in tests.
/// </summary>
internal interface IOcrEngineFactory
{
    /// <summary>
    /// Creates a new <see cref="IOcrEngine"/>.
    /// </summary>
    /// <remarks>
    /// Implementations that wrap native libraries should catch
    /// <see cref="System.DllNotFoundException"/>, <see cref="System.EntryPointNotFoundException"/>,
    /// and <see cref="System.BadImageFormatException"/> and translate them to
    /// <see cref="NetIndex.Core.Abstractions.NetIndexOcrNotInstalledException"/>.
    /// </remarks>
    IOcrEngine Create();
}
