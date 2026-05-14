using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace NetIndex.Template.Tests;

/// <summary>
/// Validates appsettings.json structure and absence of leaked secrets (AC#5, AC#9).
/// </summary>
public sealed class AppSettingsContractTests
{
    private static readonly string AppSettingsPath =
        Path.Combine(AppContext.BaseDirectory, "TemplateContent", "appsettings.json");

    private static readonly string AppSettingsDevPath =
        Path.Combine(AppContext.BaseDirectory, "TemplateContent", "appsettings.Development.json");

    private static readonly string Content = File.ReadAllText(AppSettingsPath);
    private static readonly string DevContent = File.ReadAllText(AppSettingsDevPath);

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void AppSettings_ContentFiles_Exist()
    {
        File.Exists(AppSettingsPath).Should().BeTrue(
            $"Template file not found — check Content/CopyToOutputDirectory in the .csproj. Expected: {AppSettingsPath}");
        File.Exists(AppSettingsDevPath).Should().BeTrue(
            $"Template file not found — check Content/CopyToOutputDirectory in the .csproj. Expected: {AppSettingsDevPath}");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void AppSettings_WhenLoaded_IsValidJson()
    {
        var act = () => JsonDocument.Parse(Content);
        act.Should().NotThrow("appsettings.json must be well-formed JSON");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void AppSettings_WhenLoaded_ContainsNetIndexSection()
    {
        using var doc = JsonDocument.Parse(Content);
        doc.RootElement.TryGetProperty("NetIndex", out _).Should().BeTrue(
            "appsettings.json must have a top-level 'NetIndex' section");
    }

    [Trait("Category", "PipelineContract")]
    [Theory]
    [InlineData("AzureOpenAI")]
    [InlineData("Pgvector")]
    [InlineData("Ollama")]
    [InlineData("Sqlite")]
    public void AppSettings_WhenLoaded_ContainsRequiredSubSection(string subSection)
    {
        using var doc = JsonDocument.Parse(Content);
        var netIndex = doc.RootElement.GetProperty("NetIndex");
        netIndex.TryGetProperty(subSection, out _).Should().BeTrue(
            $"NetIndex section must contain '{subSection}' sub-section");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void AppSettings_WhenLoaded_PgvectorDimensionsIs1536()
    {
        using var doc = JsonDocument.Parse(Content);
        doc.RootElement.GetProperty("NetIndex").GetProperty("Pgvector")
            .GetProperty("Dimensions").GetInt32().Should().Be(1536,
            "Pgvector Dimensions must be 1536 (Azure OpenAI ada-002 embedding size)");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void AppSettings_WhenLoaded_SqliteDimensionsIs768()
    {
        using var doc = JsonDocument.Parse(Content);
        doc.RootElement.GetProperty("NetIndex").GetProperty("Sqlite")
            .GetProperty("Dimensions").GetInt32().Should().Be(768,
            "Sqlite Dimensions must be 768 (nomic-embed-text embedding size)");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void AppSettings_WhenLoaded_AzureOpenAIStringFieldsUseAngleBracketPlaceholders()
    {
        using var doc = JsonDocument.Parse(Content);
        var azureSection = doc.RootElement.GetProperty("NetIndex").GetProperty("AzureOpenAI");
        foreach (var property in azureSection.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                var value = property.Value.GetString()!;
                (value.StartsWith("<") && value.EndsWith(">")).Should().BeTrue(
                    $"AzureOpenAI.{property.Name} must use <angle-bracket> placeholder notation, got: '{value}'");
            }
        }
    }

    [Trait("Category", "PipelineContract")]
    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void AppSettings_WhenLoaded_ContainsNoLeakedOpenAiKeys(string fileName)
    {
        var content = fileName == "appsettings.json" ? Content : DevContent;
        // Catches sk-..., sk-proj-..., sk-ant-... key formats
        Regex.IsMatch(content, @"sk-(?:proj-|ant-)?[A-Za-z0-9_-]{20,}").Should().BeFalse(
            $"{fileName} must not contain real OpenAI API keys");
    }

    [Trait("Category", "PipelineContract")]
    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void AppSettings_WhenLoaded_ContainsNoLeakedSecretStrings(string fileName)
    {
        var content = fileName == "appsettings.json" ? Content : DevContent;
        // Case-insensitive to catch Azure mixed-case keys in addition to lowercase hex
        var suspiciousMatches = Regex.Matches(content, @"\b[a-fA-F0-9]{32}\b");
        suspiciousMatches.Should().BeEmpty(
            $"{fileName} must not contain 32-char hex secret tokens (Azure keys, access tokens, etc.)");
    }

    [Trait("Category", "PipelineContract")]
    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void AppSettings_WhenLoaded_ContainsNoHardcodedPasswords(string fileName)
    {
        var content = fileName == "appsettings.json" ? Content : DevContent;
        // Matches Password= followed by at least 3 typical password characters (not <, ;, whitespace, or empty)
        Regex.IsMatch(content, @"Password\s*=\s*[A-Za-z0-9!@#$%^&*_\-]{3,}").Should().BeFalse(
            $"{fileName} must not contain a hardcoded password in a connection string");
    }
}
