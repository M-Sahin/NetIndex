using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace NetIndex.Template.Tests;

/// <summary>
/// Validates the scaffolded README.md required H2 sections (AC#6, AC#9).
/// </summary>
public sealed class ReadmeContractTests
{
    private static readonly string ReadmePath =
        Path.Combine(AppContext.BaseDirectory, "TemplateContent", "README.md");

    private static readonly string Content = File.ReadAllText(ReadmePath);

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
}
