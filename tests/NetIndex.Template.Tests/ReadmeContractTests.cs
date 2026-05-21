using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace NetIndex.Template.Tests;

/// <summary>
/// Validates the scaffolded README.md required H2 sections and local-swap narrative
/// (AC#6, AC#9, Story 4.2 AC#4, Story 4.3 AC#5, AC#6, Story 4.4 AC#4, AC#5).
/// </summary>
public sealed class ReadmeContractTests
{
    private static readonly string ReadmePath =
        Path.Combine(AppContext.BaseDirectory, "TemplateContent", "README.md");

    private static readonly string Content = File.ReadAllText(ReadmePath);

    /// <summary>
    /// Returns the text between the local-swap H2 heading and the next H2 heading
    /// (or end of file). Anchoring contract assertions to this slice keeps stray
    /// mentions in other sections from masking a missing or moved swap section.
    /// </summary>
    private static string LocalSwapSection
    {
        get
        {
            var match = Regex.Match(
                Content,
                @"^##\s*Switch to Local Development.*?(?=^##\s|\z)",
                RegexOptions.Multiline | RegexOptions.Singleline);
            return match.Success ? match.Value : string.Empty;
        }
    }

    /// <summary>
    /// Returns the text between the "Zero-Config Local Quickstart" H2 heading and
    /// the next H2 heading (or end of file). Assertions anchored to this slice
    /// cannot pass accidentally from mentions in other sections.
    /// </summary>
    private static string QuickstartSection
    {
        get
        {
            var match = Regex.Match(
                Content,
                @"^##\s*Zero-Config Local Quickstart.*?(?=^##\s|\z)",
                RegexOptions.Multiline | RegexOptions.Singleline);
            return match.Success ? match.Value : string.Empty;
        }
    }

    /// <summary>
    /// Returns the text between the "Sample API" H2 heading and the next H2 heading
    /// (or end of file). Assertions anchored to this slice cannot pass accidentally
    /// from mentions in other sections.
    /// </summary>
    private static string SampleApiSection
    {
        get
        {
            var match = Regex.Match(
                Content,
                @"^##\s*Sample API.*?(?=^##\s|\z)",
                RegexOptions.Multiline | RegexOptions.Singleline);
            return match.Success ? match.Value : string.Empty;
        }
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void Readme_ContentFile_Exists()
    {
        File.Exists(ReadmePath).Should().BeTrue(
            $"Template file not found — check Content/CopyToOutputDirectory in the .csproj. Expected: {ReadmePath}");
    }

    [Trait("Category", "PipelineContract")]
    [Theory]
    [InlineData("## What was scaffolded")]
    [InlineData("## Configure Azure OpenAI + pgvector (default)")]
    [InlineData("## Switch to Local Development (Ollama + SQLite)")]
    [InlineData("## Zero-Config Local Quickstart")]
    [InlineData("## Sample API")]
    [InlineData("## Run it")]
    [InlineData("## Next steps")]
    public void Readme_WhenLoaded_ContainsRequiredSection(string heading)
    {
        // Anchor to start-of-line so H3+ headings (### ...) don't false-pass
        var pattern = "^" + Regex.Escape(heading) + @"\s*$";
        Regex.IsMatch(Content, pattern, RegexOptions.Multiline).Should().BeTrue(
            $"README.md must contain the H2 section '{heading}' (not H3 or deeper)");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void Readme_WhenLoaded_CallsOutDenyAllTenantResolver()
    {
        Content.Should().Contain("DenyAllTenantResolver",
            "README.md must warn about the deny-all auth default");
        Content.Should().Contain("ITenantResolver",
            "README.md must instruct users to configure ITenantResolver");
        Content.Should().Contain("production",
            "README.md must call out production deployment requirement for ITenantResolver");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void Readme_WhenLoaded_LocalSwapSectionDescribesCommentAndUncomment()
    {
        var section = LocalSwapSection;
        section.Should().NotBeEmpty("the local-swap H2 section must exist");

        section.Should().Contain("comment",
            "local swap section must mention commenting out enterprise lines");
        section.Should().Contain("uncomment",
            "local swap section must mention uncommenting local lines");
        section.Should().Contain("UseAzureOpenAI",
            "local swap section must name UseAzureOpenAI as the line to comment");
        section.Should().Contain("UsePgvector",
            "local swap section must name UsePgvector as the line to comment");
        section.Should().Contain("UseOllama",
            "local swap section must name UseOllama as the line to uncomment");
        section.Should().Contain("UseSqlite",
            "local swap section must name UseSqlite as the line to uncomment");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void Readme_WhenLoaded_LocalSwapSectionIncludesOllamaPrerequisites()
    {
        var section = LocalSwapSection;
        section.Should().NotBeEmpty("the local-swap H2 section must exist");

        section.Should().Contain("ollama serve",
            "local swap section must instruct users to start Ollama");
        section.Should().Contain("ollama pull nomic-embed-text",
            "local swap section must instruct users to pull nomic-embed-text");
        section.Should().Contain("ollama pull mistral",
            "local swap section must instruct users to pull mistral");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void Readme_WhenLoaded_LocalSwapSectionIncludesSwitchBack()
    {
        var section = LocalSwapSection;
        section.Should().NotBeEmpty("the local-swap H2 section must exist");

        section.Should().Contain("switch back",
            "local swap section must explain how to switch back to Azure + pgvector");
    }

    // Story 4.4: the Zero-Config Local Quickstart section must reference the
    // updated /api/ingest and /api/query paths.
    [Trait("Category", "PipelineContract")]
    [Fact]
    public void Readme_WhenLoaded_QuickstartSectionContainsCurlExamples()
    {
        var section = QuickstartSection;
        section.Should().NotBeEmpty("the Zero-Config Local Quickstart H2 section must exist");

        section.Should().Contain("curl",
            "quickstart section must contain curl examples");
        section.Should().Contain("/api/ingest",
            "quickstart section must document the /api/ingest endpoint");
        section.Should().Contain("/api/query",
            "quickstart section must document the /api/query endpoint");
        section.Should().Contain("dotnet run",
            "quickstart section must include the dotnet run step");
    }

    // Story 4.3: the quickstart section must call out the dev-only tenant resolver.
    [Trait("Category", "PipelineContract")]
    [Fact]
    public void Readme_WhenLoaded_QuickstartSectionCallsOutDevTenantResolver()
    {
        var section = QuickstartSection;
        section.Should().NotBeEmpty("the Zero-Config Local Quickstart H2 section must exist");

        section.Should().Contain("LocalDevTenantResolver",
            "quickstart section must name LocalDevTenantResolver and explain it is dev-only");
    }

    // Story 4.4: the Sample API section must contain curl examples for all three endpoints.
    [Trait("Category", "PipelineContract")]
    [Fact]
    public void Readme_WhenLoaded_SampleApiSectionContainsThreeEndpointCurlExamples()
    {
        var section = SampleApiSection;
        section.Should().NotBeEmpty("the Sample API H2 section must exist");

        section.Should().Contain("POST /api/ingest",
            "Sample API section must document POST /api/ingest");
        section.Should().Contain("GET /api/query",
            "Sample API section must document GET /api/query");
        section.Should().Contain("POST /api/generate",
            "Sample API section must document POST /api/generate");

        var curlCount = Regex.Matches(section, @"\bcurl\b").Count;
        curlCount.Should().BeGreaterThanOrEqualTo(3,
            "Sample API section must contain at least three curl invocations (one per endpoint)");
    }
}
