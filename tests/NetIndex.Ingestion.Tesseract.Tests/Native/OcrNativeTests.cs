using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FluentAssertions;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Tesseract.Options;
using NetIndex.Testing.Common;
using MsOptions = Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace NetIndex.Ingestion.Tesseract.Tests.Native;

/// <summary>Required native OCR evidence for supported platforms.</summary>
[Trait("Category", "OcrNative")]
[Collection(TestingConstants.Collections.Tesseract)]
public sealed class OcrNativeTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initializes native tests with diagnostic output.</summary>
    public OcrNativeTests(ITestOutputHelper output) => _output = output;

    /// <summary>Proves direct PNG support with pinned bytes and semantic tokens.</summary>
    [Fact]
    public async Task ExtractAsync_PngFixture_ReturnsSemanticTokensAsync()
    {
        await using var extractor = CreateExtractor();
        using var png = GetVerifiedFixture("sample-gray.png");

        var result = await extractor.ExtractAsync(png, MediaTypes.Png);

        EmitDiagnostics(result);
        Normalize(result.Text).Should().Contain("NETINDEX OCR PNG");
        Normalize(result.Text).Should().Contain("SEMANTIC TOKEN GREEN");
        result.Pages.Should().ContainSingle();
        AssertConfidence(result);
    }

    /// <summary>Proves one-page scanned PDF OCR and engine metadata.</summary>
    [Fact]
    public async Task ExtractAsync_OnePagePdf_ReturnsSemanticTokensAsync()
    {
        await using var extractor = CreateExtractor();
        using var pdf = GetVerifiedFixture("scanned-one-page.pdf");

        var result = await extractor.ExtractAsync(pdf, MediaTypes.Pdf);

        EmitDiagnostics(result);
        Normalize(result.Text).Should().Contain("NETINDEX OCR PDF ONE");
        Normalize(result.Text).Should().Contain("SEMANTIC TOKEN BLUE");
        result.Pages.Should().ContainSingle();
        result.EngineName.Should().Be("tesseract");
        result.EngineVersion.Should().NotBeNullOrWhiteSpace();
        AssertConfidence(result);
    }

    /// <summary>Proves two-page OCR preserves source order.</summary>
    [Fact]
    public async Task ExtractAsync_TwoPagePdf_PreservesPageOrderAsync()
    {
        await using var extractor = CreateExtractor();
        using var pdf = GetVerifiedFixture("scanned-two-page.pdf");

        var result = await extractor.ExtractAsync(pdf, MediaTypes.Pdf);

        EmitDiagnostics(result);
        result.Pages.Should().HaveCount(2);
        Normalize(result.Pages[0].Text).Should().Contain("FIRST PAGE ALPHA");
        Normalize(result.Pages[1].Text).Should().Contain("SECOND PAGE BETA");
        Normalize(result.Text).IndexOf("FIRST PAGE ALPHA", StringComparison.Ordinal)
            .Should().BeLessThan(Normalize(result.Text).IndexOf("SECOND PAGE BETA", StringComparison.Ordinal));
        AssertConfidence(result);
    }

    private TesseractVisionExtractor CreateExtractor()
    {
        var tessDataPath = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
        if (string.IsNullOrWhiteSpace(tessDataPath) ||
            !File.Exists(Path.Combine(tessDataPath, "eng.traineddata")))
        {
            throw new InvalidOperationException(
                "OcrNative requires TESSDATA_PREFIX containing eng.traineddata; the CI lane must provision it.");
        }

        return new TesseractVisionExtractor(MsOptions.Options.Create(new TesseractOptions
        {
            TessDataPath = tessDataPath,
            Languages = "eng",
            RasterizationDpi = 300,
        }));
    }

    private static Stream GetVerifiedFixture(string filename)
    {
        var expected = ReadChecksumManifest()[filename];
        var stream = GetRequiredResource(filename);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        actual.Should().Be(expected, $"{filename} must match the committed checksum manifest");
        stream.Position = 0;
        return stream;
    }

    private static Dictionary<string, string> ReadChecksumManifest()
    {
        using var stream = GetRequiredResource("checksums.txt");
        using var reader = new StreamReader(stream);
        var checksums = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            checksums.Add(parts[1], parts[0]);
        }
        return checksums;
    }

    private static Stream GetRequiredResource(string filename)
    {
        var assembly = typeof(OcrNativeTests).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(filename, StringComparison.OrdinalIgnoreCase));
        return resourceName is null
            ? throw new InvalidOperationException($"Required embedded OCR fixture '{filename}' is missing.")
            : assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded OCR fixture '{filename}' could not be opened.");
    }

    private static string Normalize(string text)
        => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static void AssertConfidence(VisionExtractionResult result)
    {
        result.MeanConfidence.Should().BeInRange(0.0, 1.0);
        result.Pages.Should().AllSatisfy(page => page.Confidence.Should().BeInRange(0.0, 1.0));
    }

    private void EmitDiagnostics(VisionExtractionResult result)
    {
        _output.WriteLine($"Engine: {result.EngineName} {result.EngineVersion}");
        _output.WriteLine($"Language: {result.Language}; DPI: {result.RasterizationDpi}; Pages: {result.Pages.Count}");
        _output.WriteLine($"Mean confidence: {result.MeanConfidence:P1}");
    }
}
