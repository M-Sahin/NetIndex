#pragma warning disable CS1591
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Options;
using NetIndex.Ingestion.Strategies;
using NetIndex.Testing.Common;
using Xunit;
using Opts = Microsoft.Extensions.Options.Options;

namespace NetIndex.Ingestion.Tests.Strategies;

/// <summary>
/// Story 2.12: every strategy must emit chunks no longer than ChunkSize (4 characters per token),
/// even for long inputs without paragraph breaks, sentences or whitespace.
/// </summary>
public class ChunkSizeEnforcementTests
{
    private const int MaxChars = 800; // 200 tokens

    private static readonly ChunkingOptions Options = new(200, 20, "\n\n");

    public static TheoryData<string> Strategies => new() { "fixed", "semantic", "recursive" };

    private static IChunkingStrategy Create(string name)
    {
        var config = Opts.Create(new ChunkingConfiguration().FixedSize(200, 20));
        return name switch
        {
            "fixed" => new FixedSizeChunkingStrategy(config),
            "semantic" => new SemanticChunkingStrategy(new FakeEmbeddingGenerator(), config),
            _ => new RecursiveChunkingStrategy(new FakeEmbeddingGenerator(), config),
        };
    }

    private static string Paragraphs(int chars)
    {
        var paragraph = string.Concat(Enumerable.Repeat("Sentence about retrieval. ", 12)).TrimEnd();
        var sb = new System.Text.StringBuilder();
        while (sb.Length < chars)
        {
            sb.Append(paragraph).Append("\n\n");
        }

        return sb.ToString(0, chars);
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public async Task Paragraphs_10kChars_NoChunkExceedsLimitAsync(string name)
    {
        var chunks = (await Create(name).ChunkAsync(Paragraphs(10_000), Options)).ToList();

        chunks.Should().HaveCountGreaterThan(10);
        chunks.Should().OnlyContain(c => c.Text.Length <= MaxChars);
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public async Task NoParagraphBreaks_SplitsAtSentencesAsync(string name)
    {
        var text = string.Concat(Enumerable.Repeat("Sentence about retrieval. ", 400)).TrimEnd();

        var chunks = (await Create(name).ChunkAsync(text, Options)).ToList();

        chunks.Should().HaveCountGreaterThan(10);
        chunks.Should().OnlyContain(c => c.Text.Length <= MaxChars);
        chunks.Take(chunks.Count - 1).Should().OnlyContain(c => c.Text.EndsWith('.'), "splits land on sentence boundaries");
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public async Task NoPunctuation_SplitsAtWhitespaceAsync(string name)
    {
        var text = string.Join(' ', Enumerable.Range(0, 3000).Select(i => $"word{i}"));

        var chunks = (await Create(name).ChunkAsync(text, Options)).ToList();

        chunks.Should().OnlyContain(c => c.Text.Length <= MaxChars);
        chunks.SelectMany(c => c.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Should().OnlyContain(w => System.Text.RegularExpressions.Regex.IsMatch(w, @"^word\d+$"), "whitespace splits never cut a word");
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public async Task NoSeparatorsAtAll_HardSplitsWithOverlapAsync(string name)
    {
        var text = string.Concat(Enumerable.Range(0, 10_000).Select(i => (char)('a' + (i % 26))));

        var chunks = (await Create(name).ChunkAsync(text, Options)).ToList();

        chunks.Should().OnlyContain(c => c.Text.Length <= MaxChars);
        chunks.Count.Should().BeGreaterThanOrEqualTo(13);

        // overlap (20 tokens = 80 chars) is honoured: each chunk starts with the tail of the previous one
        for (var i = 1; i < chunks.Count; i++)
        {
            var previous = chunks[i - 1].Text;
            var tail = previous[^80..];
            chunks[i].Text.Should().StartWith(tail);
        }
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public async Task SingleHugeToken_WithEmoji_NeverSplitsASurrogatePairAsync(string name)
    {
        var text = string.Concat(Enumerable.Repeat("\U0001F600", 3000));

        var chunks = (await Create(name).ChunkAsync(text, Options)).ToList();

        chunks.Should().OnlyContain(c => c.Text.Length <= MaxChars);
        foreach (var chunk in chunks)
        {
            char.IsLowSurrogate(chunk.Text[0]).Should().BeFalse();
            char.IsHighSurrogate(chunk.Text[^1]).Should().BeFalse();
        }
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public async Task ShortText_IsUnchangedAsync(string name)
    {
        var chunks = (await Create(name).ChunkAsync("Just one short sentence.", Options)).ToList();

        chunks.Should().ContainSingle().Which.Text.Should().Be("Just one short sentence.");
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public async Task ChunkIds_AreSequentialAsync(string name)
    {
        var chunks = (await Create(name).ChunkAsync(Paragraphs(5_000), Options)).ToList();

        chunks.Select(c => c.Id).Should().Equal(Enumerable.Range(0, chunks.Count).Select(i => $"chunk_{i}"));
    }

    [Property(MaxTest = 60)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "VSTHRD002", Justification = "FsCheck Property methods are synchronous by design")]
    public Property AnyLongInput_NeverExceedsLimit_ForEveryStrategy(NonEmptyString seed, PositiveInt repeat)
    {
        var text = string.Concat(Enumerable.Repeat(seed.Get + " ", Math.Min(repeat.Get, 400) + 50));
        var noWhitespace = string.Concat(Enumerable.Repeat(
            new string(seed.Get.Where(c => !char.IsWhiteSpace(c)).ToArray()) + "x", Math.Min(repeat.Get, 400) + 50));
        var options = new ChunkingOptions(50, 5, "\n");

        return new[] { "fixed", "semantic", "recursive" }
            .All(name => new[] { text, noWhitespace }
                .All(input => Create(name).ChunkAsync(input, options).GetAwaiter().GetResult().All(c => c.Text.Length <= 200)))
            .ToProperty();
    }
}

/// <summary>Story 2.12 review fixes: Recursive semantic stage, normalized output, configuration validation.</summary>
public class RecursiveAndConfigurationReviewTests
{
    private sealed class OrthogonalEmbedder : IEmbeddingGenerator
    {
        public int Dimensions => 64;

        public Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(new float[Dimensions]);

        public Task<float[][]> GenerateBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            var vectors = texts.Select((_, i) =>
            {
                var v = new float[Dimensions];
                v[i % Dimensions] = 1f;
                return v;
            }).ToArray();
            return Task.FromResult(vectors);
        }
    }

    [Fact]
    public async Task Recursive_OversizedParagraph_UsesTheSemanticStageAndReturnsNormalizedChunksAsync()
    {
        var config = Opts.Create(new ChunkingConfiguration().FixedSize(200, 20));
        var options = new ChunkingOptions(200, 20, "\n\n");
        var text = string.Concat(Enumerable.Range(0, 120).Select(i => $"Sentence number {i} is about retrieval. "));

        var fixedChunks = (await new FixedSizeChunkingStrategy(config).ChunkAsync(text, options)).ToList();
        var recursive = (await new RecursiveChunkingStrategy(new OrthogonalEmbedder(), config).ChunkAsync(text, options)).ToList();

        recursive.Select(c => c.Text).Should().NotEqual(fixedChunks.Select(c => c.Text));
        recursive.Should().OnlyContain(c => c.Text.Length <= 800);
        recursive.Should().OnlyContain(c => c.Embedding == null && c.DocumentId == "pending" && c.Metadata == null);
        recursive.Select(c => c.Id).Should().Equal(Enumerable.Range(0, recursive.Count).Select(i => $"chunk_{i}"));
    }

    [Fact]
    public void Semantic_And_Recursive_ValidateSizesLikeFixedSize()
    {
        FluentActions.Invoking(() => new ChunkingConfiguration().Semantic(0, 0)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new ChunkingConfiguration().Recursive(-5, 0)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new ChunkingConfiguration().Semantic(100, 100)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new ChunkingConfiguration().Recursive(100, -1)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new ChunkingConfiguration().Semantic(int.MaxValue, 0)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new ChunkingConfiguration().FixedSize(int.MaxValue, 0)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new ChunkingConfiguration().Semantic(200, 20)).Should().NotThrow();
        FluentActions.Invoking(() => new ChunkingConfiguration().Recursive()).Should().NotThrow();
    }

    [Theory]
    [InlineData("fixed")]
    [InlineData("semantic")]
    [InlineData("recursive")]
    public async Task Strategies_RejectOverlapNotBelowSizeAndOverflowingSizesAsync(string name)
    {
        var config = Opts.Create(new ChunkingConfiguration());
        IChunkingStrategy strategy = name switch
        {
            "fixed" => new FixedSizeChunkingStrategy(config),
            "semantic" => new SemanticChunkingStrategy(new OrthogonalEmbedder(), config),
            _ => new RecursiveChunkingStrategy(new OrthogonalEmbedder(), config),
        };

        await FluentActions.Awaiting(() => strategy.ChunkAsync("text", new ChunkingOptions(10, 10, "\n")))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => strategy.ChunkAsync("text", new ChunkingOptions(int.MaxValue, 0, "\n")))
            .Should().ThrowAsync<ArgumentException>();
    }
}
