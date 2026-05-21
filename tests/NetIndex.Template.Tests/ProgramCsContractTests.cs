using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace NetIndex.Template.Tests;

/// <summary>
/// Validates the scaffolded Program.cs structure (AC#4, AC#9, Story 4.2 AC#1-2, Story 4.3 AC#4, AC#6, Story 4.4 AC#5).
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

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void ProgramCs_WhenLoaded_SwapBlockFollowsEnterpriseBlockAdjacently()
    {
        // Inside AddNetIndex(...) the four provider calls must appear in this
        // order, with the active enterprise block and the commented local block
        // each contiguous and separated only by a short local-dev marker:
        //   - active   netIndex.UseAzureOpenAI(...)
        //   - active   netIndex.UsePgvector(...)
        //   - (optional blank line)
        //   - 1-2 line "🔁 LOCAL DEV" marker comment
        //   - commented netIndex.UseOllama(...)
        //   - commented netIndex.UseSqlite(...)
        var lines = Content.Replace("\r\n", "\n").Split('\n');

        int activeAzureIdx = Array.FindIndex(lines,
            l => Regex.IsMatch(l, @"^\s*netIndex\.UseAzureOpenAI\("));
        int activePgIdx = Array.FindIndex(lines,
            l => Regex.IsMatch(l, @"^\s*netIndex\.UsePgvector\("));
        int commentedOllamaIdx = Array.FindIndex(lines,
            l => Regex.IsMatch(l, @"^\s*//[^/]\s*netIndex\.UseOllama\("));
        int commentedSqliteIdx = Array.FindIndex(lines,
            l => Regex.IsMatch(l, @"^\s*//[^/]\s*netIndex\.UseSqlite\("));

        activeAzureIdx.Should().BeGreaterThan(-1, "active UseAzureOpenAI must exist");
        activePgIdx.Should().BeGreaterThan(-1, "active UsePgvector must exist");
        commentedOllamaIdx.Should().BeGreaterThan(-1, "commented UseOllama must exist");
        commentedSqliteIdx.Should().BeGreaterThan(-1, "commented UseSqlite must exist");

        activeAzureIdx.Should().BeLessThan(activePgIdx,
            "active UseAzureOpenAI must appear before active UsePgvector");
        activePgIdx.Should().BeLessThan(commentedOllamaIdx,
            "active UsePgvector must appear before the commented UseOllama swap line");
        commentedOllamaIdx.Should().BeLessThan(commentedSqliteIdx,
            "commented UseOllama must appear before commented UseSqlite");

        // Between active UsePgvector and commented UseOllama: at most a blank
        // line plus a two-line "🔁 LOCAL DEV" marker (gap <= 4 line indices).
        (commentedOllamaIdx - activePgIdx).Should().BeLessThanOrEqualTo(4,
            "the swap block must be directly adjacent to the active enterprise block " +
            "(at most a blank line and a two-line local-dev marker between them)");

        // Commented UseSqlite must sit directly under commented UseOllama —
        // nothing may be inserted between the two swap-block lines.
        (commentedSqliteIdx - commentedOllamaIdx).Should().Be(1,
            "the commented UseSqlite swap line must be directly under commented UseOllama");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void ProgramCs_WhenLoaded_CommentedSwapCallsBindToConfiguration()
    {
        // Uncommenting the swap block must produce calls that read configuration
        // from the documented sections — otherwise the swap silently produces a
        // broken application even though the lines look right.
        Regex.IsMatch(
            Content,
            @"//[^/]\s*netIndex\.UseOllama\(\s*builder\.Configuration\.GetSection\(\s*""NetIndex:Ollama""\s*\)\s*\)"
        ).Should().BeTrue(
            "commented UseOllama swap line must bind to builder.Configuration.GetSection(\"NetIndex:Ollama\")");

        Regex.IsMatch(
            Content,
            @"//[^/]\s*netIndex\.UseSqlite\(\s*builder\.Configuration\.GetSection\(\s*""NetIndex:Sqlite""\s*\)\s*\)"
        ).Should().BeTrue(
            "commented UseSqlite swap line must bind to builder.Configuration.GetSection(\"NetIndex:Sqlite\")");
    }

    [Trait("Category", "PipelineContract")]
    [Fact]
    public void ProgramCs_WhenLoaded_ContainsLocalDevMarkerComment()
    {
        // The "🔁 LOCAL DEV" marker is the anchor the README narrative points at —
        // deleting it would orphan the swap instructions without breaking any
        // other test.
        Content.Should().Contain("LOCAL DEV",
            "Program.cs must contain the LOCAL DEV swap marker comment");
    }

    // Patch: Story 4.3 code review — assert .Build() is chained on AddNetIndex().
    // The missing-.Build() bug fix from Dev Notes can silently regress; this test
    // prevents that. Anchors on the lambda's closing brace + AddNetIndex's closing
    // paren + .Build() so the assertion fails if the chain is dropped (a stray
    // `var app = builder.Build();` elsewhere in the file cannot satisfy this).
    [Trait("Category", "PipelineContract")]
    [Fact]
    public void ProgramCs_WhenLoaded_ChainsBuildAfterAddNetIndex()
    {
        Regex.IsMatch(
            Content,
            @"\}\s*\)\s*\.Build\s*\(\s*\)",
            RegexOptions.Singleline
        ).Should().BeTrue(
            "Program.cs must chain .Build() on the AddNetIndex(...) call — " +
            "without it, INetIndexPipeline is never registered in DI");
    }

    // Story 4.4: the /api/ingest POST endpoint must be present and active.
    [Trait("Category", "PipelineContract")]
    [Fact]
    public void ProgramCs_WhenLoaded_ContainsActiveIngestEndpoint()
    {
        Regex.IsMatch(Content, @"^\s*app\.MapPost\(\s*""/api/ingest""", RegexOptions.Multiline).Should().BeTrue(
            "Program.cs must contain an active app.MapPost(\"/api/ingest\", ...) endpoint");
    }

    // Story 4.4: the /api/query GET endpoint must be present and active.
    [Trait("Category", "PipelineContract")]
    [Fact]
    public void ProgramCs_WhenLoaded_ContainsActiveQueryEndpoint()
    {
        Regex.IsMatch(Content, @"^\s*app\.MapGet\(\s*""/api/query""", RegexOptions.Multiline).Should().BeTrue(
            "Program.cs must contain an active app.MapGet(\"/api/query\", ...) endpoint");
    }

    // Story 4.4: the /api/generate POST endpoint must be present and active.
    [Trait("Category", "PipelineContract")]
    [Fact]
    public void ProgramCs_WhenLoaded_ContainsActiveGenerateEndpoint()
    {
        Regex.IsMatch(Content, @"^\s*app\.MapPost\(\s*""/api/generate""", RegexOptions.Multiline).Should().BeTrue(
            "Program.cs must contain an active app.MapPost(\"/api/generate\", ...) endpoint");
    }

    // Story 4.4: replaces NoGenerateOrApiPrefixedEndpoints with a precise allowlist.
    // Forbids the un-prefixed Story 4.3 routes (must be removed) and any undocumented
    // /api/* route beyond the three canonical ones.
    [Trait("Category", "PipelineContract")]
    [Fact]
    public void ProgramCs_WhenLoaded_NoUnprefixedOrUndocumentedApiEndpoints()
    {
        // The un-prefixed Story 4.3 routes must be gone.
        Regex.IsMatch(Content, @"\.Map(Get|Post|Put|Delete|Patch)\s*\(\s*""/ingest""").Should().BeFalse(
            "Program.cs must not contain an un-prefixed /ingest endpoint — it was replaced by /api/ingest in Story 4.4");

        Regex.IsMatch(Content, @"\.Map(Get|Post|Put|Delete|Patch)\s*\(\s*""/query""").Should().BeFalse(
            "Program.cs must not contain an un-prefixed /query endpoint — it was replaced by /api/query in Story 4.4");

        // Only the three documented /api/* routes are permitted.
        Regex.IsMatch(Content, @"app\.Map(Get|Post|Put|Delete|Patch)\s*\(\s*""/api/(?!ingest""|query""|generate"")\w+").Should().BeFalse(
            "Program.cs must not contain undocumented /api/* endpoints — only /api/ingest, /api/query, /api/generate are permitted");
    }

    // Story 4.4: Results.Problem( must appear at least three times (one per error
    // category: 401, 502, 500) and the canonical error type URLs must appear at
    // least three times.
    [Trait("Category", "PipelineContract")]
    [Fact]
    public void ProgramCs_WhenLoaded_UsesResultsProblemForErrors()
    {
        var problemMatches = Regex.Matches(Content, @"Results\.Problem\(");
        problemMatches.Count.Should().BeGreaterThanOrEqualTo(3,
            "Program.cs must use Results.Problem(...) for error handling — at least once per error category (401, 502, 500)");

        // Each of the four canonical error categories must be present.
        foreach (var category in new[] { "validation", "authorization", "provider", "internal" })
        {
            Content.Should().Contain($"https://netindex.dev/errors/{category}",
                $"Program.cs must include the '{category}' error type URI");
        }
    }

    // Story 4.3: an explicit non-deny-all ITenantResolver must be registered
    // so the dev-swap path does not throw NetIndexAuthorizationException on every call.
    [Trait("Category", "PipelineContract")]
    [Fact]
    public void ProgramCs_WhenLoaded_RegistersNonDenyAllTenantResolver()
    {
        // Match AddSingleton<ITenantResolver, SomeClass> where SomeClass is not DenyAllTenantResolver.
        // The resolver class name is left open so a future story can swap LocalDevTenantResolver
        // for a different dev resolver without breaking this test.
        Regex.IsMatch(
            Content,
            @"AddSingleton<\s*ITenantResolver\s*,\s*(?!DenyAllTenantResolver)\w+\s*>",
            RegexOptions.Multiline
        ).Should().BeTrue(
            "Program.cs must register an ITenantResolver that is not DenyAllTenantResolver " +
            "so the local-dev pipeline operations can proceed");
    }
}
