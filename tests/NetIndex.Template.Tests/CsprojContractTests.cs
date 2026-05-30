using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace NetIndex.Template.Tests;

/// <summary>
/// Validates the scaffolded csproj structure (AC#7, AC#9).
/// </summary>
public sealed class CsprojContractTests
{
    private static readonly string CsprojPath =
        Path.Combine(AppContext.BaseDirectory, "TemplateContent", "NetIndex.Template.csproj");

    private static readonly string Content = File.ReadAllText(CsprojPath);
    private static readonly XDocument CsprojXml = XDocument.Parse(Content);

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void Csproj_ContentFile_Exists()
    {
        File.Exists(CsprojPath).Should().BeTrue(
            $"Template file not found — check Content/CopyToOutputDirectory in the .csproj. Expected: {CsprojPath}");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void Csproj_WhenLoaded_UsesMicrosoftNETSdkWeb()
    {
        Content.Should().Contain("Microsoft.NET.Sdk.Web");
    }

    [Trait("Category", "PipelineContract")]
    [Theory]
    [InlineData("NetIndex.Core")]
    [InlineData("NetIndex.AspNetCore")]
    [InlineData("NetIndex.Providers.AzureOpenAI")]
    [InlineData("NetIndex.Storage.Pgvector")]
    [InlineData("NetIndex.Providers.Ollama")]
    [InlineData("NetIndex.Storage.Sqlite")]
    public void Csproj_WhenLoaded_ReferencesRequiredNetIndexPackage(string packageId)
    {
        // Parse XML to ensure the reference is a live element, not a commented-out line
        CsprojXml.Descendants("PackageReference")
            .Any(e => e.Attribute("Include")?.Value == packageId)
            .Should().BeTrue(
                $"scaffolded csproj must have a live <PackageReference Include=\"{packageId}\" />");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void Csproj_WhenLoaded_UsesVersionToken_NotHardcodedVersion()
    {
        // The six NetIndex.* PackageReferences must use the dotnet-new substitution token,
        // not a hardcoded version — so the smoke gate and consumer installs reference the
        // just-published version rather than always pulling 0.9.1.
        Content.Should().Contain("NETINDEX_PKG_VERSION",
            "content csproj must use the NETINDEX_PKG_VERSION sentinel so dotnet new substitutes the real version");
        Content.Should().NotContain("Version=\"0.9.1\"",
            "hardcoded 0.9.1 must be replaced by the NETINDEX_PKG_VERSION token");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void Csproj_WhenLoaded_AllSixNetIndexVersionsUseToken()
    {
        var tokenCount = CsprojXml.Descendants("PackageReference")
            .Count(e => e.Attribute("Include")?.Value?.StartsWith("NetIndex.") == true
                     && e.Attribute("Version")?.Value == "NETINDEX_PKG_VERSION");
        tokenCount.Should().Be(6,
            "all six NetIndex.* PackageReferences must carry the NETINDEX_PKG_VERSION token");
    }
}
