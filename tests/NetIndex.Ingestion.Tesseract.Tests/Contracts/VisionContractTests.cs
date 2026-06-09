using System.Collections.Generic;
using FluentAssertions;
using NetIndex.Core.Abstractions;
using Xunit;

namespace NetIndex.Ingestion.Tesseract.Tests.Contracts;

/// <summary>
/// Smoke tests for the managed vision contracts in Core.Abstractions.
/// No native dependencies required.
/// </summary>
public sealed class VisionContractTests
{
    /// <summary>Verifies that <see cref="VisionPageResult"/> stores all constructor properties.</summary>
    [Fact]
    public void VisionPageResult_StoresProperties()
    {
        var page = new VisionPageResult(PageNumber: 1, Text: "hello", Confidence: 0.95);

        page.PageNumber.Should().Be(1);
        page.Text.Should().Be("hello");
        page.Confidence.Should().Be(0.95);
    }

    /// <summary>Verifies that <see cref="VisionExtractionResult"/> stores all constructor properties.</summary>
    [Fact]
    public void VisionExtractionResult_StoresProperties()
    {
        var pages = new List<VisionPageResult>
        {
            new(1, "page one", 0.9),
            new(2, "page two", 0.8)
        };
        var result = new VisionExtractionResult(
            "page one\npage two",
            MeanConfidence: 0.85,
            Pages: pages,
            EngineName: "tesseract",
            EngineVersion: "5.3.0",
            Language: "eng",
            RasterizationDpi: 300);

        result.Text.Should().Be("page one\npage two");
        result.MeanConfidence.Should().Be(0.85);
        result.Pages.Should().HaveCount(2);
        result.Pages[0].PageNumber.Should().Be(1);
        result.Pages[1].PageNumber.Should().Be(2);
        result.EngineName.Should().Be("tesseract");
        result.EngineVersion.Should().Be("5.3.0");
        result.Language.Should().Be("eng");
        result.RasterizationDpi.Should().Be(300);
    }

    /// <summary>Verifies that <see cref="MediaTypes"/> constants have the correct MIME-type values.</summary>
    [Fact]
    public void MediaTypes_HasExpectedConstants()
    {
        MediaTypes.Pdf.Should().Be("application/pdf");
        MediaTypes.Png.Should().Be("image/png");
        MediaTypes.Jpeg.Should().Be("image/jpeg");
    }

    /// <summary>Verifies that <see cref="IVisionExtractor"/> is an interface type.</summary>
    [Fact]
    public void IVisionExtractor_IsAnInterface()
    {
        typeof(IVisionExtractor).IsInterface.Should().BeTrue();
    }

    /// <summary>Verifies that <see cref="IVisionExtractor.ExtractAsync"/> has the correct return type.</summary>
    [Fact]
    public void IVisionExtractor_HasExtractAsyncMethod()
    {
        var method = typeof(IVisionExtractor).GetMethod("ExtractAsync");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(System.Threading.Tasks.Task<VisionExtractionResult>));
    }
}
