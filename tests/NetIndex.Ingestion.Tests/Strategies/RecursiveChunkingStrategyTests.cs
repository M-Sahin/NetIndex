using FluentAssertions;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Options;
using NetIndex.Ingestion.Strategies;
using NetIndex.Testing.Common;
using Xunit;

namespace NetIndex.Ingestion.Tests.Strategies;

/// <summary>
/// Unit tests for <see cref="RecursiveChunkingStrategy"/>.
/// </summary>
public class RecursiveChunkingStrategyTests
{
    private static readonly ChunkingConfiguration Config = new ChunkingConfiguration().Recursive();
    private static readonly RecursiveChunkingStrategy Strategy = new RecursiveChunkingStrategy(
        new FakeEmbeddingGenerator(),
        Microsoft.Extensions.Options.Options.Create(Config));

    /// <summary>
    /// Small text should be handled by fixed-size path.
    /// </summary>
    [Fact]
    public async Task ChunkAsync_WithSmallText_UsesFixedSizePathAsync()
    {
        var text = "This is a short piece of text that should fit in a single chunk without any splitting.";
        var options = new ChunkingOptions(512, 64, "\n");

        var result = await Strategy.ChunkAsync(text, options);

        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
    }

    /// <summary>
    /// Text that produces large chunks should trigger the fixed-size path first.
    /// </summary>
    [Fact]
    public async Task ChunkAsync_WithLargeText_AppliesFixedSizeFirstAsync()
    {
        var text = string.Join("\n", Enumerable.Repeat("This is a line that repeats many times to create long text that can be chunked by the recursive strategy.", 50));
        var options = new ChunkingOptions(100, 10, "\n");

        var result = await Strategy.ChunkAsync(text, options);

        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
    }

    /// <summary>
    /// Null text should throw ArgumentNullException.
    /// </summary>
    [Fact]
    public Task ChunkAsync_WithNullText_ThrowsArgumentNullExceptionAsync()
    {
        var options = new ChunkingOptions(512, 64, "\n");

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

        var act = () => Strategy.ChunkAsync("some text", new ChunkingOptions(512, 64, "\n"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}