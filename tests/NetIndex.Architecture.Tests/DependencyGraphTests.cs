using NetArchTest.Rules;
using System.Reflection;
using System.Xml.Linq;
using Xunit;
using Xunit.Abstractions;

namespace NetIndex.Architecture.Tests;

/// <summary>
/// Enforce layer dependency rules at build time using NetArchTest.
///
/// Layer structure (top = most abstract):
///   ┌─────────────────────┐
///   │   AspNetCore         │  ← Presentation (depends on Core + Core.Abstractions)
///   ├─────────────────────┤
///   │   Core               │  ← Domain (depends on Core.Abstractions only)
///   ├─────────────────────┤
///   │   Core.Abstractions  │  ← Contracts (depends on nothing)
///   └─────────────────────┘
///
///   Providers.*, Storage.*, Ingestion.* are sibling layers that depend
///   on Core.Abstractions only — never on each other or on Core/AspNetCore.
/// </summary>
[Trait("Category", "ArchContract")]
public class DependencyGraphTests
{
    private readonly ITestOutputHelper _output;
    private readonly TestContext _ctx;

    /// <summary>
    /// Load a referenced assembly by its simple name.
    /// All source projects are project-referenced from this test project,
    /// so they are available in the default load context.
    /// </summary>
    private static string GetAssemblyPath(string assemblyName)
    {
        var asm = System.Reflection.Assembly.Load(assemblyName);
        Assert.NotNull(asm.Location);
        return asm.Location;
    }

    public DependencyGraphTests(ITestOutputHelper output)
    {
        _output = output;
        _ctx = new TestContext(this, output);
    }

    // ─── Helpers ───

    private void AssertNoDependency(string assemblyName, string forbiddenNamespace, string ruleDescription)
    {
        var asmPath = GetAssemblyPath(assemblyName);
        var result = Types.FromFile(asmPath)
            .Should()
            .NotHaveDependencyOn(forbiddenNamespace)
            .GetResult();

        _ctx.Log(result, ruleDescription);
        Assert.True(result.IsSuccessful, ruleDescription);
    }

    private void AssertNoDependencies(string assemblyName, string[] forbiddenNamespaces, string ruleDescription)
    {
        var asmPath = GetAssemblyPath(assemblyName);
        var result = Types.FromFile(asmPath)
            .Should()
            .NotHaveDependencyOnAny(forbiddenNamespaces)
            .GetResult();

        _ctx.Log(result, ruleDescription);
        Assert.True(result.IsSuccessful, ruleDescription);
    }

    private void AssertNoAssemblyReference(string assemblyName, string forbiddenAssemblyName, string ruleDescription)
    {
        var asmPath = GetAssemblyPath(assemblyName);
        var referencedAssemblies = Assembly.LoadFrom(asmPath).GetReferencedAssemblies();
        var hasForbiddenReference = referencedAssemblies.Any(a =>
            string.Equals(a.Name, forbiddenAssemblyName, StringComparison.Ordinal));

        Assert.False(hasForbiddenReference, ruleDescription);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NetIndex.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Cannot locate repo root (NetIndex.sln not found)");
    }

    private static IEnumerable<string> EnumerateCsprojsUnder(string baseDir)
        => Directory.EnumerateFiles(baseDir, "*.csproj", SearchOption.AllDirectories)
                    .Where(static p => !p.Contains("/bin/") && !p.Contains("/obj/")
                                    && !p.Contains("\\bin\\") && !p.Contains("\\obj\\")
                                    && !p.Contains("/content/") && !p.Contains("\\content\\"));

    // ─── AC3: Core.Abstractions must be completely independent ───

    [Fact]
    public void CoreAbstractions_ShouldNot_DependOn_AnySrcProject()
    {
        // Note: We cannot check "NetIndex.Core" here because NetArchTest uses namespace-prefix
        // matching — "NetIndex.Core" would also match "NetIndex.Core.Abstractions" itself.
        // Core.Abstractions has zero project references to other src projects, so the compiler
        // already enforces that invariant. We verify the other (non-prefix) namespaces here.
        AssertNoDependencies(
            "NetIndex.Core.Abstractions",
            ["NetIndex.AspNetCore", "NetIndex.Providers", "NetIndex.Storage", "NetIndex.Ingestion"],
            "Core.Abstractions should not depend on any other src project");
    }

    // ─── AC5: Core depends only on Core.Abstractions ───

    [Fact]
    public void Core_ShouldNot_DependOn_AspNetCore()
    {
        AssertNoDependency("NetIndex.Core", "NetIndex.AspNetCore", "Core should not depend on AspNetCore");
    }

    [Fact]
    public void Core_ShouldNot_DependOn_Providers()
    {
        AssertNoDependency("NetIndex.Core", "NetIndex.Providers", "Core should not depend on Providers");
    }

    [Fact]
    public void Core_ShouldNot_DependOn_Storage()
    {
        AssertNoDependency("NetIndex.Core", "NetIndex.Storage", "Core should not depend on Storage");
    }

    [Fact]
    public void Core_ShouldNot_DependOn_Ingestion()
    {
        AssertNoDependency("NetIndex.Core", "NetIndex.Ingestion", "Core should not depend on Ingestion");
    }

    // ─── AC4: Providers depend only on Core.Abstractions ───

    [Fact]
    public void Providers_ShouldNot_DependOn_AspNetCore()
    {
        foreach (var provider in new[] { "NetIndex.Providers.OpenAI", "NetIndex.Providers.Ollama", "NetIndex.Providers.AzureOpenAI" })
        {
            AssertNoDependency(provider, "NetIndex.AspNetCore", $"{provider} should not depend on AspNetCore");
        }
    }

    [Fact]
    public void Providers_ShouldNot_DependOn_Core()
    {
        foreach (var provider in new[] { "NetIndex.Providers.OpenAI", "NetIndex.Providers.Ollama", "NetIndex.Providers.AzureOpenAI" })
        {
            AssertNoAssemblyReference(provider,
                "NetIndex.Core",
                $"{provider} should not reference NetIndex.Core assembly");
        }
    }

    [Fact]
    public void Providers_ShouldNot_DependOn_Storage()
    {
        foreach (var provider in new[] { "NetIndex.Providers.OpenAI", "NetIndex.Providers.Ollama", "NetIndex.Providers.AzureOpenAI" })
        {
            AssertNoDependency(provider, "NetIndex.Storage", $"{provider} should not depend on Storage");
        }
    }

    [Fact]
    public void Providers_ShouldNot_DependOn_Ingestion()
    {
        foreach (var provider in new[] { "NetIndex.Providers.OpenAI", "NetIndex.Providers.Ollama", "NetIndex.Providers.AzureOpenAI" })
        {
            AssertNoDependency(provider, "NetIndex.Ingestion", $"{provider} should not depend on Ingestion");
        }
    }

    // ─── AC4: Storage depend only on Core.Abstractions ───

    [Fact]
    public void Storage_ShouldNot_DependOn_AspNetCore()
    {
        foreach (var storage in new[] { "NetIndex.Storage.InMemory", "NetIndex.Storage.Sqlite", "NetIndex.Storage.Pgvector" })
        {
            AssertNoDependency(storage, "NetIndex.AspNetCore", $"{storage} should not depend on AspNetCore");
        }
    }

    [Fact]
    public void Storage_ShouldNot_DependOn_Core()
    {
        foreach (var storage in new[] { "NetIndex.Storage.InMemory", "NetIndex.Storage.Sqlite", "NetIndex.Storage.Pgvector" })
        {
            AssertNoAssemblyReference(storage,
                "NetIndex.Core",
                $"{storage} should not reference NetIndex.Core assembly");
        }
    }

    [Fact]
    public void Storage_ShouldNot_DependOn_Providers()
    {
        foreach (var storage in new[] { "NetIndex.Storage.InMemory", "NetIndex.Storage.Sqlite", "NetIndex.Storage.Pgvector" })
        {
            AssertNoDependency(storage, "NetIndex.Providers", $"{storage} should not depend on Providers");
        }
    }

    [Fact]
    public void Storage_ShouldNot_DependOn_Ingestion()
    {
        foreach (var storage in new[] { "NetIndex.Storage.InMemory", "NetIndex.Storage.Sqlite", "NetIndex.Storage.Pgvector" })
        {
            AssertNoDependency(storage, "NetIndex.Ingestion", $"{storage} should not depend on Ingestion");
        }
    }

    // ─── AC4: Ingestion depend only on Core.Abstractions ───

    private static readonly string[] AllIngestionProjects = new[]
    {
        "NetIndex.Ingestion.Pdf", "NetIndex.Ingestion.Docx", "NetIndex.Ingestion.Markdown",
        "NetIndex.Ingestion.Tesseract", "NetIndex.Ingestion"
    };

    [Fact]
    public void Ingestion_ShouldNot_DependOn_AspNetCore()
    {
        foreach (var ingestion in AllIngestionProjects)
        {
            AssertNoDependency(ingestion, "NetIndex.AspNetCore", $"{ingestion} should not depend on AspNetCore");
        }
    }

    [Fact]
    public void Ingestion_ShouldNot_DependOn_Core()
    {
        foreach (var ingestion in AllIngestionProjects)
        {
            AssertNoAssemblyReference(ingestion,
                "NetIndex.Core",
                $"{ingestion} should not reference NetIndex.Core assembly");
        }
    }

    [Fact]
    public void Ingestion_ShouldNot_DependOn_Providers()
    {
        foreach (var ingestion in AllIngestionProjects)
        {
            AssertNoDependency(ingestion, "NetIndex.Providers", $"{ingestion} should not depend on Providers");
        }
    }

    [Fact]
    public void Ingestion_ShouldNot_DependOn_Storage()
    {
        foreach (var ingestion in AllIngestionProjects)
        {
            AssertNoDependency(ingestion, "NetIndex.Storage", $"{ingestion} should not depend on Storage");
        }
    }

    // ─── AC5: AspNetCore depends only on Core.Abstractions + Core ───

    [Fact]
    public void AspNetCore_ShouldNot_DependOn_Providers()
    {
        AssertNoDependency("NetIndex.AspNetCore", "NetIndex.Providers", "AspNetCore should not depend on Providers");
    }

    [Fact]
    public void AspNetCore_ShouldNot_DependOn_Storage()
    {
        AssertNoDependency("NetIndex.AspNetCore", "NetIndex.Storage", "AspNetCore should not depend on Storage");
    }

    [Fact]
    public void AspNetCore_ShouldNot_DependOn_Ingestion()
    {
        AssertNoDependency("NetIndex.AspNetCore", "NetIndex.Ingestion", "AspNetCore should not depend on Ingestion");
    }

    // ─── Epic 8: Integrations.* depend only on Core.Abstractions ───

    [Fact]
    public void SemanticKernel_ShouldNot_DependOn_Core()
    {
        AssertNoAssemblyReference("NetIndex.SemanticKernel",
            "NetIndex.Core",
            "SemanticKernel should not reference NetIndex.Core assembly");
    }

    [Fact]
    public void SemanticKernel_ShouldNot_DependOn_AspNetCoreOrSiblingPackages()
    {
        AssertNoDependencies(
            "NetIndex.SemanticKernel",
            ["NetIndex.AspNetCore", "NetIndex.Providers", "NetIndex.Storage", "NetIndex.Ingestion"],
            "SemanticKernel should not depend on AspNetCore, Providers, Storage, or Ingestion");
    }

    [Fact]
    public void ExistingPackages_ShouldNot_DependOn_SemanticKernel()
    {
        var assemblies = new[]
        {
            "NetIndex.Core.Abstractions", "NetIndex.Core", "NetIndex.AspNetCore",
            "NetIndex.Providers.OpenAI", "NetIndex.Providers.Ollama", "NetIndex.Providers.AzureOpenAI",
            "NetIndex.Storage.InMemory", "NetIndex.Storage.Sqlite", "NetIndex.Storage.Pgvector",
            "NetIndex.Ingestion.Pdf", "NetIndex.Ingestion.Docx", "NetIndex.Ingestion.Markdown",
            "NetIndex.Ingestion.Tesseract", "NetIndex.Ingestion"
        };

        foreach (var assembly in assemblies)
        {
            AssertNoDependency(assembly, "NetIndex.SemanticKernel", $"{assembly} should not depend on NetIndex.SemanticKernel");
        }
    }

    [Fact]
    public void SemanticKernelCsproj_ReferencesOnlyCoreAbstractionsProject()
    {
        var root = FindRepoRoot();
        var csprojPath = Path.Combine(root, "src", "Integrations", "NetIndex.SemanticKernel", "NetIndex.SemanticKernel.csproj");
        var xml = XDocument.Load(csprojPath);

        var projectReferences = xml.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .ToList();

        Assert.Single(projectReferences);
        Assert.EndsWith("NetIndex.Core.Abstractions.csproj", projectReferences[0], StringComparison.Ordinal);
    }

    [Fact]
    public void SemanticKernelCsproj_ReferencesOnlySemanticKernelCorePackage()
    {
        var root = FindRepoRoot();
        var csprojPath = Path.Combine(root, "src", "Integrations", "NetIndex.SemanticKernel", "NetIndex.SemanticKernel.csproj");
        var xml = XDocument.Load(csprojPath);

        var semanticKernelPackages = xml.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(include => include is not null && include.StartsWith("Microsoft.SemanticKernel", StringComparison.Ordinal))
            .ToList();

        Assert.Single(semanticKernelPackages);
        Assert.Equal("Microsoft.SemanticKernel.Core", semanticKernelPackages[0]);
    }

    [Fact]
    public void NoOtherProjectCsproj_ReferencesSemanticKernelPackagesOrProject()
    {
        var root = FindRepoRoot();
        var csprojs = EnumerateCsprojsUnder(Path.Combine(root, "src"))
            .Concat(EnumerateCsprojsUnder(Path.Combine(root, "templates")))
            .Where(path => Path.GetFileNameWithoutExtension(path) != "NetIndex.SemanticKernel")
            .ToList();

        Assert.NotEmpty(csprojs);

        var violations = new List<string>();
        foreach (var path in csprojs)
        {
            var xml = XDocument.Load(path);

            var hasSemanticKernelPackage = xml.Descendants()
                .Where(e => e.Name.LocalName == "PackageReference")
                .Select(e => e.Attribute("Include")?.Value)
                .Any(include => include is not null && include.StartsWith("Microsoft.SemanticKernel", StringComparison.Ordinal));

            var hasSemanticKernelProjectReference = xml.Descendants()
                .Where(e => e.Name.LocalName == "ProjectReference")
                .Select(e => e.Attribute("Include")?.Value)
                .Any(include => include is not null && include.Contains("NetIndex.SemanticKernel", StringComparison.Ordinal));

            if (hasSemanticKernelPackage || hasSemanticKernelProjectReference)
            {
                violations.Add(path.Replace(root, ""));
            }
        }

        Assert.True(violations.Count == 0,
            $"csproj(s) outside NetIndex.SemanticKernel must not reference Semantic Kernel packages or the SemanticKernel project:\n  {string.Join("\n  ", violations)}");
    }

    // ─── Epic 8: OpenAI provider csproj boundary ───

    [Fact]
    public void OpenAICsproj_ReferencesOnlyCoreAbstractionsProject()
    {
        var root = FindRepoRoot();
        var csprojPath = Path.Combine(root, "src", "Providers", "NetIndex.Providers.OpenAI", "NetIndex.Providers.OpenAI.csproj");
        var xml = XDocument.Load(csprojPath);

        var projectReferences = xml.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .ToList();

        Assert.Single(projectReferences);
        Assert.EndsWith("NetIndex.Core.Abstractions.csproj", projectReferences[0], StringComparison.Ordinal);
    }

    [Fact]
    public void OpenAICsproj_ReferencesOnlyApprovedPackages()
    {
        var root = FindRepoRoot();
        var csprojPath = Path.Combine(root, "src", "Providers", "NetIndex.Providers.OpenAI", "NetIndex.Providers.OpenAI.csproj");
        var xml = XDocument.Load(csprojPath);

        var expectedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "OpenAI",
            "Microsoft.Extensions.Configuration.Binder",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Options",
            "Microsoft.Extensions.Options.ConfigurationExtensions",
        };

        var actualPackages = new HashSet<string>(
            xml.Descendants()
               .Where(e => e.Name.LocalName == "PackageReference")
               .Select(e => e.Attribute("Include")?.Value)
               .Where(name => name is not null)!,
            StringComparer.OrdinalIgnoreCase);

        var extras = actualPackages.Except(expectedPackages, StringComparer.OrdinalIgnoreCase).ToList();
        var missing = expectedPackages.Except(actualPackages, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.True(extras.Count == 0 && missing.Count == 0,
            $"NetIndex.Providers.OpenAI.csproj package references do not match the approved set. " +
            $"Extra: [{string.Join(", ", extras)}]; Missing: [{string.Join(", ", missing)}]");
    }

    [Fact]
    public void NoOtherProjectCsproj_ReferencesOpenAIProviderProject()
    {
        var root = FindRepoRoot();
        var csprojs = EnumerateCsprojsUnder(Path.Combine(root, "src"))
            .Concat(EnumerateCsprojsUnder(Path.Combine(root, "templates")))
            .Where(path => Path.GetFileNameWithoutExtension(path) != "NetIndex.Providers.OpenAI")
            .ToList();

        Assert.NotEmpty(csprojs);

        var violations = new List<string>();
        foreach (var path in csprojs)
        {
            var xml = XDocument.Load(path);
            var hasRef = xml.Descendants()
                .Where(e => e.Name.LocalName == "ProjectReference")
                .Select(e => e.Attribute("Include")?.Value)
                .Any(include => include is not null && include.Contains("NetIndex.Providers.OpenAI", StringComparison.Ordinal));
            if (hasRef)
            {
                violations.Add(path.Replace(root, ""));
            }
        }

        Assert.True(violations.Count == 0,
            $"csproj(s) outside NetIndex.Providers.OpenAI must not reference the OpenAI provider project:\n  {string.Join("\n  ", violations)}");
    }

    // ─── Test context helper ───

    private sealed class TestContext
    {
        private readonly DependencyGraphTests _test;
        private readonly ITestOutputHelper _output;

        public TestContext(DependencyGraphTests test, ITestOutputHelper output)
        {
            _test = test;
            _output = output;
        }

        public void Log(TestResult result, string description)
        {
            _output.WriteLine($"{description}: {(result.IsSuccessful ? "PASS" : "FAIL")}");
            if (!result.IsSuccessful)
            {
                foreach (var typeName in result.FailingTypeNames)
                {
                    _output.WriteLine($"  - {typeName}");
                }
            }
        }
    }
}
