using System;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Tesseract.Options;
using NSubstitute;
using Xunit;

namespace NetIndex.Ingestion.Tesseract.Tests.Registration;

/// <summary>
/// Managed tests for <see cref="NetIndexBuilderExtensions.UseTesseract"/>. No native library required.
/// </summary>
public sealed class BuilderExtensionsTests : IDisposable
{
    private readonly string _tempDir;

    /// <summary>Creates a temp tessdata directory used by tests that resolve IOptions.Value.</summary>
    public BuilderExtensionsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"netindex-bld-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        File.WriteAllBytes(Path.Combine(_tempDir, "eng.traineddata"), [0x00]);
        File.WriteAllBytes(Path.Combine(_tempDir, "fra.traineddata"), [0x00]);
    }

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);
    /// <summary>Verifies that UseTesseract registers IVisionExtractor as TesseractVisionExtractor.</summary>
    [Fact]
    public void UseTesseract_RegistersVisionExtractor()
    {
        var (_, services) = BuilderWithServices();

        services.Should().Contain(s =>
            s.ServiceType == typeof(IVisionExtractor) &&
            s.ImplementationType == typeof(TesseractVisionExtractor));
    }

    /// <summary>Verifies that UseTesseract registers exactly one options validator.</summary>
    [Fact]
    public void UseTesseract_RegistersOneValidator()
    {
        var (_, services) = BuilderWithServices();

        services.Where(s => s.ServiceType == typeof(IValidateOptions<TesseractOptions>))
            .Should().HaveCount(1);
    }

    /// <summary>Verifies that calling UseTesseract twice does not duplicate the validator.</summary>
    [Fact]
    public void UseTesseract_CalledTwice_RegistersSingleValidator()
    {
        var (builder, services) = BuilderWithServices();

        builder.UseTesseract();

        services.Where(s => s.ServiceType == typeof(IValidateOptions<TesseractOptions>))
            .Should().HaveCount(1);
    }

    /// <summary>Verifies that calling UseTesseract twice does not register a second IVisionExtractor.</summary>
    [Fact]
    public void UseTesseract_CalledTwice_RegistersSingleExtractor()
    {
        var (builder, services) = BuilderWithServices();

        builder.UseTesseract();

        services.Where(s => s.ServiceType == typeof(IVisionExtractor))
            .Should().HaveCount(1);
    }

    /// <summary>Verifies that the configure delegate is applied to TesseractOptions.</summary>
    [Fact]
    public void UseTesseract_WithConfigure_AppliesOptions()
    {
        var (_, services) = BuilderWithServices(opts =>
        {
            opts.TessDataPath = _tempDir;
            opts.RasterizationDpi = 150;
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TesseractOptions>>().Value;

        options.RasterizationDpi.Should().Be(150);
    }

    /// <summary>Verifies that a null configure delegate is accepted without error; default values are preserved.</summary>
    [Fact]
    public void UseTesseract_NullConfigure_RegistersDefaultOptions()
    {
        var (builder, services) = BuilderWithServices(configure: null);
        builder.UseTesseract(opts => opts.TessDataPath = _tempDir); // satisfy validation

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TesseractOptions>>().Value;

        options.Languages.Should().Be("eng");
        options.RasterizationDpi.Should().Be(300);
    }

    /// <summary>Verifies that repeated calls accumulate options in registration order, with last-write winning.</summary>
    [Fact]
    public void UseTesseract_CalledTwiceWithConfigure_LastValueWins()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);

        builder.UseTesseract(opts =>
        {
            opts.TessDataPath = _tempDir;
            opts.RasterizationDpi = 150;
        });
        builder.UseTesseract(opts => opts.RasterizationDpi = 200);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TesseractOptions>>().Value;

        options.RasterizationDpi.Should().Be(200);
    }

    /// <summary>Verifies that a null builder argument throws ArgumentNullException.</summary>
    [Fact]
    public void UseTesseract_NullBuilder_ThrowsArgumentNullException()
    {
        INetIndexBuilder? nullBuilder = null;

        var act = () => nullBuilder!.UseTesseract();

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>Verifies invalid OCR options fail from the real NetIndex Build call.</summary>
    [Fact]
    public void UseTesseract_InvalidOptions_FailDuringNetIndexBuild()
    {
        var services = new ServiceCollection();
        var builder = services.AddNetIndex(configure => configure.UseTesseract(options =>
        {
            options.TessDataPath = Path.Combine(_tempDir, "missing");
        }));

        var act = () => builder.Build();

        act.Should().Throw<NetIndexConfigurationException>()
            .WithInnerException<OptionsValidationException>();
    }

    /// <summary>Verifies repeated registration adds only one build validator.</summary>
    [Fact]
    public void UseTesseract_CalledTwice_RegistersSingleBuildValidator()
    {
        var (builder, services) = BuilderWithServices();

        builder.UseTesseract();

        services.Where(s => s.ServiceType == typeof(INetIndexBuildValidator))
            .Should().HaveCount(1);
    }

    private static (INetIndexBuilder builder, IServiceCollection services) BuilderWithServices(
        Action<TesseractOptions>? configure = null)
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);

        builder.UseTesseract(configure);

        return (builder, services);
    }
}
