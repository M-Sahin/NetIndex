using Xunit;

namespace NetIndex.Architecture.Tests;

/// <summary>
/// PR-gate invariants for the nightly RAG evaluation workflow: it must target the evaluation
/// project directly, use the exact Evaluation filter, guard against a silent zero-test pass,
/// and keep uploading results unconditionally. The PR workflow must keep excluding the
/// Evaluation category while still running the evaluation project's untagged tests.
/// </summary>
[Trait("Category", "ArchContract")]
public class EvaluationWorkflowContractTests
{
    private const string EvaluationProjectPath = "tests/NetIndex.Evaluation.Tests/NetIndex.Evaluation.Tests.csproj";

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

    private static string ReadEvaluationWorkflow()
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, ".github", "workflows", "evaluation.yml"));
    }

    private static string ReadPrWorkflow()
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, ".github", "workflows", "pr.yml"));
    }

    [Fact]
    public void EvaluationWorkflow_TargetsTheEvaluationProjectDirectly()
    {
        var content = ReadEvaluationWorkflow();

        Assert.True(content.Contains(EvaluationProjectPath, StringComparison.Ordinal),
            $"evaluation.yml must run 'dotnet test {EvaluationProjectPath}' directly instead of a solution-wide filter that can pass with zero tests.");
    }

    [Fact]
    public void EvaluationWorkflow_UsesTheExactEvaluationCategoryFilter()
    {
        var content = ReadEvaluationWorkflow();

        Assert.True(content.Contains("--filter \"Category=Evaluation\"", StringComparison.Ordinal),
            "evaluation.yml must filter the evaluation project run with exactly --filter \"Category=Evaluation\".");
    }

    [Fact]
    public void EvaluationWorkflow_GuardsAgainstASilentZeroTestPass()
    {
        var content = ReadEvaluationWorkflow();

        Assert.True(content.Contains("<UnitTestResult", StringComparison.Ordinal),
            "evaluation.yml must fail the job if no <UnitTestResult> entries are present in the TRX output " +
            "(VSTest returns success for a filter that selects zero tests).");
        Assert.True(content.Contains("exit 1", StringComparison.Ordinal),
            "evaluation.yml's zero-test guard must actually fail the job (exit 1), not just log a warning.");
    }

    [Fact]
    public void EvaluationWorkflow_DoesNotReferenceExternalApiEnvironmentConfiguration()
    {
        var content = ReadEvaluationWorkflow();

        Assert.False(content.Contains("EVALUATION_MODE", StringComparison.Ordinal),
            "evaluation.yml must not reference EVALUATION_MODE or other external-API environment configuration; " +
            "Story 8.3's retrieval evaluation is fully offline.");
    }

    [Fact]
    public void EvaluationWorkflow_UploadsResultsUnconditionally()
    {
        var content = ReadEvaluationWorkflow();

        Assert.True(content.Contains("actions/upload-artifact", StringComparison.Ordinal),
            "evaluation.yml must upload the results/ directory as an artifact.");

        var uploadIndex = content.IndexOf("actions/upload-artifact", StringComparison.Ordinal);
        var stepStart = content.LastIndexOf("- name:", uploadIndex, StringComparison.Ordinal);
        var stepContent = content[stepStart..(content.IndexOf("path: results/", uploadIndex, StringComparison.Ordinal) + "path: results/".Length)];

        Assert.True(stepContent.Contains("if: always()", StringComparison.Ordinal),
            "evaluation.yml's upload-artifact step must run with if: always() so failed runs still upload results/.");
        Assert.True(stepContent.Contains("path: results/", StringComparison.Ordinal),
            "evaluation.yml's upload-artifact step must upload the results/ directory.");
    }

    [Fact]
    public void PrWorkflow_StillExcludesTheEvaluationCategory()
    {
        var content = ReadPrWorkflow();

        Assert.True(content.Contains("Category!=Evaluation", StringComparison.Ordinal),
            "pr.yml's unit-test filter must keep excluding Category=Evaluation so the nightly quality gate " +
            "never runs as part of the PR gate.");
    }
}
