using NetArchTest.Rules;
using System.Reflection;
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

    [Fact]
    public void Ingestion_ShouldNot_DependOn_AspNetCore()
    {
        foreach (var ingestion in new[] { "NetIndex.Ingestion.Pdf", "NetIndex.Ingestion.Docx", "NetIndex.Ingestion.Tesseract" })
        {
            AssertNoDependency(ingestion, "NetIndex.AspNetCore", $"{ingestion} should not depend on AspNetCore");
        }
    }

    [Fact]
    public void Ingestion_ShouldNot_DependOn_Core()
    {
        foreach (var ingestion in new[] { "NetIndex.Ingestion.Pdf", "NetIndex.Ingestion.Docx", "NetIndex.Ingestion.Tesseract" })
        {
            AssertNoAssemblyReference(ingestion,
                "NetIndex.Core",
                $"{ingestion} should not reference NetIndex.Core assembly");
        }
    }

    [Fact]
    public void Ingestion_ShouldNot_DependOn_Providers()
    {
        foreach (var ingestion in new[] { "NetIndex.Ingestion.Pdf", "NetIndex.Ingestion.Docx", "NetIndex.Ingestion.Tesseract" })
        {
            AssertNoDependency(ingestion, "NetIndex.Providers", $"{ingestion} should not depend on Providers");
        }
    }

    [Fact]
    public void Ingestion_ShouldNot_DependOn_Storage()
    {
        foreach (var ingestion in new[] { "NetIndex.Ingestion.Pdf", "NetIndex.Ingestion.Docx", "NetIndex.Ingestion.Tesseract" })
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
