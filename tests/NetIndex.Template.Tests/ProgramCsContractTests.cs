using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace NetIndex.Template.Tests;

/// <summary>
/// Validates the scaffolded Program.cs structure (AC#4, AC#9).
/// </summary>
public sealed class ProgramCsContractTests
{
    private static readonly string ProgramCsPath =
        Path.Combine(AppContext.BaseDirectory, "TemplateContent", "Program.cs");

    private static readonly string Content = File.ReadAllText(ProgramCsPath);

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void ProgramCs_ContentFile_Exists()
    {
        File.Exists(ProgramCsPath).Should().BeTrue(
            $"Template file not found — check Content/CopyToOutputDirectory in the .csproj. Expected: {ProgramCsPath}");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void ProgramCs_WhenLoaded_ContainsAddNetIndex()
    {
        Content.Should().Contain("AddNetIndex(");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void ProgramCs_WhenLoaded_ContainsActiveUseAzureOpenAI()
    {
        // Must be an uncommented call — a line starting with // is not active
        Regex.IsMatch(Content, @"^\s*netIndex\.UseAzureOpenAI\(", RegexOptions.Multiline).Should().BeTrue(
            "Program.cs must contain an active (uncommented) UseAzureOpenAI( call");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void ProgramCs_WhenLoaded_ContainsActiveUsePgvector()
    {
        Regex.IsMatch(Content, @"^\s*netIndex\.UsePgvector\(", RegexOptions.Multiline).Should().BeTrue(
            "Program.cs must contain an active (uncommented) UsePgvector( call");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void ProgramCs_WhenLoaded_ContainsCommentedSwapBlockForUseOllama()
    {
        // Single-slash comment only — excludes /// doc comments via [^/]
        Regex.IsMatch(Content, @"^\s*//[^/]\s*netIndex\.UseOllama\(", RegexOptions.Multiline).Should().BeTrue(
            "Program.cs must contain a commented swap-block line for UseOllama");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void ProgramCs_WhenLoaded_ContainsCommentedSwapBlockForUseSqlite()
    {
        Regex.IsMatch(Content, @"^\s*//[^/]\s*netIndex\.UseSqlite\(", RegexOptions.Multiline).Should().BeTrue(
            "Program.cs must contain a commented swap-block line for UseSqlite");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void ProgramCs_WhenLoaded_ContainsSectionHeaderComments()
    {
        Content.Should().Contain("// --- Services ---");
        Content.Should().Contain("// --- Pipeline ---");
        Content.Should().Contain("// --- Endpoints ---");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void ProgramCs_WhenLoaded_ContainsHealthEndpoint()
    {
        Content.Should().Contain("/health");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void ProgramCs_WhenLoaded_ContainsNoHardcodedSecretsOrEndpoints()
    {
        Regex.IsMatch(Content, @"sk-[A-Za-z0-9]{20,}").Should().BeFalse(
            "Program.cs must not contain hardcoded API keys");
        Regex.IsMatch(Content, @"""https://[a-z0-9-]+\.openai\.azure\.com/""").Should().BeFalse(
            "Program.cs must not contain a hardcoded Azure OpenAI endpoint");
        Regex.IsMatch(Content, @"Password\s*=\s*""[^<]").Should().BeFalse(
            "Program.cs must not contain a hardcoded connection string password");
    }
}
