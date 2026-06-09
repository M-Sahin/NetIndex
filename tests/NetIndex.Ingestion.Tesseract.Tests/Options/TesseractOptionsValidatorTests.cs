using System;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NetIndex.Ingestion.Tesseract.Options;
using Xunit;

namespace NetIndex.Ingestion.Tesseract.Tests.Options;

/// <summary>
/// Managed tests for <see cref="TesseractOptionsValidator"/>. No native library required.
/// </summary>
public sealed class TesseractOptionsValidatorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TesseractOptionsValidator _sut = new();

    /// <summary>Creates a temp directory with an eng.traineddata stub for happy-path tests.</summary>
    public TesseractOptionsValidatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"netindex-tess-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        File.WriteAllBytes(Path.Combine(_tempDir, "eng.traineddata"), [0x00]);
    }

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    /// <summary>Verifies that valid, complete options pass validation.</summary>
    [Fact]
    public void Validate_ValidOptions_Succeeds()
    {
        var opts = ValidOpts();

        var result = _sut.Validate(null, opts);

        result.Succeeded.Should().BeTrue();
    }

    /// <summary>Verifies that an empty TessDataPath produces a failure.</summary>
    [Fact]
    public void Validate_EmptyTessDataPath_Fails()
    {
        var opts = ValidOpts();
        opts.TessDataPath = "";

        var result = _sut.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("TessDataPath");
    }

    /// <summary>Verifies that a whitespace-only TessDataPath produces a failure.</summary>
    [Fact]
    public void Validate_WhitespaceTessDataPath_Fails()
    {
        var opts = ValidOpts();
        opts.TessDataPath = "   ";

        var result = _sut.Validate(null, opts);

        result.Failed.Should().BeTrue();
    }

    /// <summary>Verifies that a DPI below 72 produces a failure.</summary>
    [Fact]
    public void Validate_DpiBelow72_Fails()
    {
        var opts = ValidOpts();
        opts.RasterizationDpi = 71;

        var result = _sut.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("RasterizationDpi");
    }

    /// <summary>Verifies that a DPI above 600 produces a failure.</summary>
    [Fact]
    public void Validate_DpiAbove600_Fails()
    {
        var opts = ValidOpts();
        opts.RasterizationDpi = 601;

        var result = _sut.Validate(null, opts);

        result.Failed.Should().BeTrue();
    }

    /// <summary>Verifies that DPI at the lower bound (72) passes.</summary>
    [Fact]
    public void Validate_DpiAt72_Succeeds()
    {
        var opts = ValidOpts();
        opts.RasterizationDpi = 72;

        var result = _sut.Validate(null, opts);

        result.Succeeded.Should().BeTrue();
    }

    /// <summary>Verifies that DPI at the upper bound (600) passes.</summary>
    [Fact]
    public void Validate_DpiAt600_Succeeds()
    {
        var opts = ValidOpts();
        opts.RasterizationDpi = 600;

        var result = _sut.Validate(null, opts);

        result.Succeeded.Should().BeTrue();
    }

    /// <summary>Verifies that a zero MaxInputBytes produces a failure.</summary>
    [Fact]
    public void Validate_ZeroMaxInputBytes_Fails()
    {
        var opts = ValidOpts();
        opts.MaxInputBytes = 0;

        var result = _sut.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("MaxInputBytes");
    }

    /// <summary>Verifies that a negative MaxInputBytes produces a failure.</summary>
    [Fact]
    public void Validate_NegativeMaxInputBytes_Fails()
    {
        var opts = ValidOpts();
        opts.MaxInputBytes = -1;

        var result = _sut.Validate(null, opts);

        result.Failed.Should().BeTrue();
    }

    /// <summary>Verifies that a zero MaxPages produces a failure.</summary>
    [Fact]
    public void Validate_ZeroMaxPages_Fails()
    {
        var opts = ValidOpts();
        opts.MaxPages = 0;

        var result = _sut.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("MaxPages");
    }

    /// <summary>Verifies that a zero MaxPixelsPerPage produces a failure.</summary>
    [Fact]
    public void Validate_ZeroMaxPixelsPerPage_Fails()
    {
        var opts = ValidOpts();
        opts.MaxPixelsPerPage = 0;

        var result = _sut.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("MaxPixelsPerPage");
    }

    /// <summary>Verifies that a non-existent TessDataPath directory produces a failure.</summary>
    [Fact]
    public void Validate_NonExistentTessDataPath_Fails()
    {
        var opts = ValidOpts();
        opts.TessDataPath = Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}");

        var result = _sut.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("does not exist");
    }

    /// <summary>Verifies that an empty Languages value produces a failure.</summary>
    [Fact]
    public void Validate_EmptyLanguages_Fails()
    {
        var opts = ValidOpts();
        opts.Languages = "";

        var result = _sut.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Languages");
    }

    /// <summary>Verifies that a missing .traineddata file produces a failure.</summary>
    [Fact]
    public void Validate_MissingTrainedDataFile_Fails()
    {
        var opts = ValidOpts();
        opts.Languages = "fra"; // no fra.traineddata created in temp dir

        var result = _sut.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("fra.traineddata");
    }

    /// <summary>Verifies that multiple languages pass when all traineddata files exist.</summary>
    [Fact]
    public void Validate_MultipleLanguagesAllPresent_Succeeds()
    {
        File.WriteAllBytes(Path.Combine(_tempDir, "fra.traineddata"), [0x00]);
        var opts = ValidOpts();
        opts.Languages = "eng+fra";

        var result = _sut.Validate(null, opts);

        result.Succeeded.Should().BeTrue();
    }

    /// <summary>Verifies that TESSDATA_PREFIX conflicting with TessDataPath produces a failure.</summary>
    [Fact]
    public void Validate_ConflictingTessDataPrefix_Fails()
    {
        using var envScope = new EnvVarScope("TESSDATA_PREFIX", "/some/other/path");
        var opts = ValidOpts(); // TessDataPath != "/some/other/path"

        var result = _sut.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("TESSDATA_PREFIX");
    }

    /// <summary>Verifies that TESSDATA_PREFIX matching TessDataPath (case-insensitive) does not conflict.</summary>
    [Fact]
    public void Validate_TessDataPrefixMatchingPath_Succeeds()
    {
        using var envScope = new EnvVarScope("TESSDATA_PREFIX", _tempDir);
        var opts = ValidOpts();

        var result = _sut.Validate(null, opts);

        result.Succeeded.Should().BeTrue();
    }

    /// <summary>Verifies empty or malformed language segments are rejected.</summary>
    [Theory]
    [InlineData("+")]
    [InlineData("eng+")]
    [InlineData("+eng")]
    [InlineData("eng++fra")]
    [InlineData("../eng")]
    public void Validate_MalformedLanguageList_Fails(string languages)
    {
        var opts = ValidOpts();
        opts.Languages = languages;

        var result = _sut.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("invalid language token");
    }

    private TesseractOptions ValidOpts() => new()
    {
        TessDataPath = _tempDir,
        Languages = "eng",
        RasterizationDpi = 300,
        MaxInputBytes = 52_428_800,
        MaxPages = 100,
        MaxPixelsPerPage = 50_000_000,
    };

    /// <summary>Saves and restores an environment variable for test isolation.</summary>
    private sealed class EnvVarScope : IDisposable
    {
        private readonly string _key;
        private readonly string? _previous;

        internal EnvVarScope(string key, string? value)
        {
            _key = key;
            _previous = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_key, _previous);
    }
}
