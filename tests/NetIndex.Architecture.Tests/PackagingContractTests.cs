using System.Xml.Linq;
using Xunit;

namespace NetIndex.Architecture.Tests;

/// <summary>
/// PR-gate packaging invariants: packability flags, per-project metadata, and release pipeline shape.
/// </summary>
[Trait("Category", "ArchContract")]
public class PackagingContractTests
{
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

    private static bool CsprojHasElement(string path, string elementName, string? value = null)
    {
        var xml = XDocument.Load(path);
        var elements = xml.Descendants().Where(e => e.Name.LocalName == elementName).ToList();
        if (!elements.Any())
        {
            return false;
        }

        if (value != null)
        {
            return elements.Any(e => e.Value.Trim().Equals(value, StringComparison.OrdinalIgnoreCase));
        }

        return true;
    }

    [Fact]
    public void EveryProductionCsproj_IsPackable_OrInheritsTrue()
    {
        var root = FindRepoRoot();
        var allProd = EnumerateCsprojsUnder(Path.Combine(root, "src"))
            .Concat(EnumerateCsprojsUnder(Path.Combine(root, "templates")))
            .ToList();

        Assert.NotEmpty(allProd);

        var violations = new List<string>();
        foreach (var path in allProd)
        {
            var xml = XDocument.Load(path);
            var isPackable = xml.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "IsPackable");
            if (isPackable != null && isPackable.Value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(path.Replace(root, ""));
            }
        }

        Assert.True(violations.Count == 0,
            $"Production csproj(s) unexpectedly marked IsPackable=false:\n  {string.Join("\n  ", violations)}");
    }

    [Fact]
    public void EveryTestCsproj_HasIsPackableFalse()
    {
        var root = FindRepoRoot();
        var allTest = EnumerateCsprojsUnder(Path.Combine(root, "tests"))
            .Concat(EnumerateCsprojsUnder(Path.Combine(root, "benchmarks")))
            .ToList();

        Assert.NotEmpty(allTest);

        var violations = allTest
            .Where(path => !CsprojHasElement(path, "IsPackable", "false"))
            .Select(path => path.Replace(root, ""))
            .ToList();

        Assert.True(violations.Count == 0,
            $"Test/benchmark csproj(s) missing <IsPackable>false</IsPackable>:\n  {string.Join("\n  ", violations)}");
    }

    [Fact]
    public void ReleaseWorkflow_PacksEveryShippablePackage()
    {
        var root = FindRepoRoot();
        var content = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));

        Assert.True(content.Contains("dotnet pack NetIndex.sln"),
            "release.yml must use 'dotnet pack NetIndex.sln' (sln-wide) instead of a per-project list that can drift");
    }

    [Fact]
    public void ReleaseWorkflow_PushStepIsTagOrDispatchGated()
    {
        var root = FindRepoRoot();
        var content = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));

        Assert.False(content.Contains("if: false"),
            "release.yml must not have 'if: false' — the publish gate must not be permanently disabled");

        var hasTagGate = content.Contains("startsWith(github.ref, 'refs/tags/v')");
        // boolean workflow_dispatch inputs are real booleans in the `inputs` context — compare with
        // `== true`, not the string `== 'true'` (which coerces to false and silently disables the gate).
        var hasDispatchGate = content.Contains("inputs.publish == true");

        Assert.True(hasTagGate || hasDispatchGate,
            "release.yml push step must be gated by startsWith(github.ref, 'refs/tags/v') or inputs.publish == true");
    }

    [Fact]
    public void ReleaseWorkflow_IncludesSymbolPackagesAndSourceLink()
    {
        var root = FindRepoRoot();

        var props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));

        // Symbol packages are controlled by IncludeSymbols=true in Directory.Build.props (preferred over
        // --include-symbols CLI flag, which cannot be overridden per-project for projects with no build output).
        Assert.True(props.Contains("IncludeSymbols>true"),
            "Directory.Build.props must set IncludeSymbols=true for library projects");
        Assert.True(props.Contains("SymbolPackageFormat>snupkg"),
            "Directory.Build.props must set SymbolPackageFormat=snupkg");
        Assert.True(props.Contains("PublishRepositoryUrl>true"),
            "Directory.Build.props must set PublishRepositoryUrl=true");
        Assert.True(props.Contains("EmbedUntrackedSources>true"),
            "Directory.Build.props must set EmbedUntrackedSources=true");

        var targets = File.ReadAllText(Path.Combine(root, "Directory.Build.targets"));
        Assert.True(targets.Contains("Microsoft.SourceLink.GitHub"),
            "Directory.Build.targets must reference Microsoft.SourceLink.GitHub");
    }

    [Fact]
    public void EveryPackableCsproj_HasTitleAndPackageTags()
    {
        var root = FindRepoRoot();
        var allProd = EnumerateCsprojsUnder(Path.Combine(root, "src"))
            .Concat(EnumerateCsprojsUnder(Path.Combine(root, "templates")))
            .ToList();

        var missingTitle = allProd
            .Where(p => !CsprojHasElement(p, "Title"))
            .Select(p => p.Replace(root, ""))
            .ToList();

        var missingTags = allProd
            .Where(p => !CsprojHasElement(p, "PackageTags"))
            .Select(p => p.Replace(root, ""))
            .ToList();

        Assert.True(missingTitle.Count == 0,
            $"Production csproj(s) missing <Title>:\n  {string.Join("\n  ", missingTitle)}");
        Assert.True(missingTags.Count == 0,
            $"Production csproj(s) missing <PackageTags>:\n  {string.Join("\n  ", missingTags)}");
    }

    [Fact]
    public void EveryPackableCsproj_DescriptionIsNotGeneric()
    {
        var root = FindRepoRoot();
        const string genericDesc = "NetIndex - LlamaIndex for .NET";

        var violations = EnumerateCsprojsUnder(Path.Combine(root, "src"))
            .Select(path => (path, xml: XDocument.Load(path)))
            .Select(t => (t.path, desc: t.xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "Description")))
            .Where(t => t.desc != null && t.desc.Value.Trim().StartsWith(genericDesc, StringComparison.Ordinal))
            .Select(t => t.path.Replace(root, ""))
            .ToList();

        Assert.True(violations.Count == 0,
            $"src csproj(s) still use the generic description — each shipped library must override <Description>:\n  {string.Join("\n  ", violations)}");
    }

    [Fact]
    public void EveryPackableCsproj_HasReadmeFile()
    {
        var root = FindRepoRoot();
        var allProd = EnumerateCsprojsUnder(Path.Combine(root, "src"))
            .Concat(EnumerateCsprojsUnder(Path.Combine(root, "templates")))
            .ToList();

        var missing = allProd
            .Where(path => !File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "README.md")))
            .Select(path => path.Replace(root, ""))
            .ToList();

        Assert.True(missing.Count == 0,
            $"Production project(s) missing README.md at their project root:\n  {string.Join("\n  ", missing)}");
    }
}
