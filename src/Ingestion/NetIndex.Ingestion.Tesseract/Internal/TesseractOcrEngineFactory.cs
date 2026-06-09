using System;
using NetIndex.Core.Abstractions;
using TesseractOCR;

namespace NetIndex.Ingestion.Tesseract.Internal;

/// <summary>
/// Creates <see cref="TesseractOcrEngine"/> instances backed by the native Tesseract library.
/// Native library loading is deferred until <see cref="Create"/> is first called.
/// </summary>
internal sealed class TesseractOcrEngineFactory : IOcrEngineFactory
{
    private static readonly string OsArchGuide =
        OperatingSystem.IsWindows()
            ? "Install the Visual C++ 2022 Redistributable and ensure the TesseractOCR package DLLs are present."
            : "Run: sudo apt-get install -y tesseract-ocr leptonica-dev (and libtesseract-dev on some distros). " +
              "Ensure loader aliases are in place: sudo ldconfig";

    private readonly string _tessDataPath;
    private readonly string _languages;

    internal TesseractOcrEngineFactory(string tessDataPath, string languages)
    {
        _tessDataPath = tessDataPath;
        _languages = languages;
    }

    public IOcrEngine Create()
    {
        try
        {
            var engine = new Engine(_tessDataPath, _languages);
            return new TesseractOcrEngine(engine);
        }
        catch (Exception ex) when (
            ex is DllNotFoundException or
            EntryPointNotFoundException or
            BadImageFormatException or
            TypeInitializationException)
        {
            throw new NetIndexOcrNotInstalledException(
                $"Tesseract native library could not be loaded on {RuntimeInfo()}: {ex.Message}. {OsArchGuide}",
                requiredPackage: "NetIndex.Ingestion.Tesseract",
                installInstructions: OsArchGuide,
                innerException: ex);
        }
    }

    private static string RuntimeInfo()
        => $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription} / {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}";
}
