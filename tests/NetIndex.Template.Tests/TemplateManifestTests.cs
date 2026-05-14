using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace NetIndex.Template.Tests;

/// <summary>
/// Validates the .template.config/template.json manifest shape (AC#3, AC#9).
/// </summary>
public sealed class TemplateManifestTests
{
    private static readonly string TemplateJsonPath =
        Path.Combine(AppContext.BaseDirectory, "TemplateContent", ".template.config", "template.json");

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void TemplateJson_ContentFile_Exists()
    {
        File.Exists(TemplateJsonPath).Should().BeTrue(
            $"Template manifest not found — check Content/CopyToOutputDirectory in the .csproj. Expected: {TemplateJsonPath}");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void TemplateJson_WhenLoaded_IsValidJson()
    {
        var json = File.ReadAllText(TemplateJsonPath);
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow("template.json must be well-formed JSON");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void TemplateJson_WhenLoaded_DeclaresShortNameNetindex()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(TemplateJsonPath));
        doc.RootElement.GetProperty("shortName").GetString().Should().Be("netindex");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void TemplateJson_WhenLoaded_DeclaresSourceName()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(TemplateJsonPath));
        doc.RootElement.GetProperty("sourceName").GetString().Should().Be("NetIndex.Template");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void TemplateJson_WhenLoaded_DeclaresIdentity()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(TemplateJsonPath));
        doc.RootElement.GetProperty("identity").GetString().Should().Be("NetIndex.Template");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void TemplateJson_WhenLoaded_SetsPreferNameDirectory()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(TemplateJsonPath));
        doc.RootElement.GetProperty("preferNameDirectory").GetBoolean().Should().BeTrue();
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void TemplateJson_WhenLoaded_DeclaresExpectedName()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(TemplateJsonPath));
        doc.RootElement.GetProperty("name").GetString()
            .Should().Be("NetIndex Enterprise RAG (Azure OpenAI + pgvector)");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void TemplateJson_WhenLoaded_DeclaresClassifications()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(TemplateJsonPath));
        var classifications = doc.RootElement.GetProperty("classifications")
            .EnumerateArray().Select(e => e.GetString()!).ToList();
        classifications.Should().Contain("Web").And.Contain("RAG").And.Contain("AI");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void TemplateJson_WhenLoaded_DeclaresTags()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(TemplateJsonPath));
        var tags = doc.RootElement.GetProperty("tags");
        tags.GetProperty("language").GetString().Should().Be("C#");
        tags.GetProperty("type").GetString().Should().Be("project");
    }
}
