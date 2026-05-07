using FluentAssertions;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Options;
using NetIndex.Ingestion.Strategies;
using Xunit;

namespace NetIndex.Ingestion.Tests.Strategies;

/// <summary>
/// Unit tests for <see cref="FixedSizeChunkingStrategy"/>.
/// </summary>
public class FixedSizeChunkingStrategyTests
{
    private static readonly ChunkingConfiguration Config = new ChunkingConfiguration().FixedSize(100, 10);
    private static readonly FixedSizeChunkingStrategy Strategy = new FixedSizeChunkingStrategy(Microsoft.Extensions.Options.Options.Create(Config));

    /// <summary>
    /// Basic happy path: valid input should return chunks.
    /// </summary>
    [Fact]
    public async Task ChunkAsync_WithValidInput_ReturnsChunksAsync()
    {
        var text = string.Join("\n", Enumerable.Repeat("This is a sentence with enough words to create multiple chunks in the fixed size strategy.", 20));
        var options = new ChunkingOptions(100, 10, "\n");

        var result = await Strategy.ChunkAsync(text, options);

        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(c => c.Id.Should().NotBeNullOrEmpty());
    }

    /// <summary>
    /// Overlap verification: consecutive chunks should share content.
    /// </summary>
    [Fact]
    public async Task ChunkAsync_WithOverlap_ProducesOverlappingChunksAsync()
    {
        var text = string.Join("\n", Enumerable.Repeat("Line of text that adds more content to the chunk.", 30));
        var options = new ChunkingOptions(50, 5, "\n");

        var result = (await Strategy.ChunkAsync(text, options)).ToList();
        if (result.Count < 2)
        {
            return;
        }

        for (var i = 1; i < result.Count; i++)
        {
            var previousText = result[i - 1].Text;
            var currentText = result[i].Text;

            var overlapContent = previousText.Length > 20
                ? previousText[^Math.Min(20, previousText.Length)..]
                : previousText;

            currentText.Should().Contain(overlapContent[..Math.Min(overlapContent.Length, currentText.Length)]);
        }
    }

    /// <summary>
    /// Empty text should return an empty collection — no content to chunk.
    /// </summary>
    [Fact]
    public async Task ChunkAsync_WithEmptyText_ReturnsEmptyAsync()
    {
        var options = new ChunkingOptions(100, 10, "\n");

        var result = await Strategy.ChunkAsync(string.Empty, options);

        result.Should().BeEmpty();
    }

    /// <summary>
    /// Null text should throw ArgumentNullException.
    /// </summary>
    [Fact]
    public Task ChunkAsync_WithNullText_ThrowsArgumentNullExceptionAsync()
    {
        var options = new ChunkingOptions(100, 10, "\n");

        var act = () => Strategy.ChunkAsync(null!, options);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// Pre-cancelled token should throw OperationCanceledException.
    /// </summary>
    [Fact]
    public async Task ChunkAsync_CancellationRequested_ThrowsOperationCanceledExceptionAsync()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => Strategy.ChunkAsync("some text", new ChunkingOptions(100, 10, "\n"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}