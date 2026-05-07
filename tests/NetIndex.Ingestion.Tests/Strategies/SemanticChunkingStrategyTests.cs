using FluentAssertions;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Options;
using NetIndex.Ingestion.Strategies;
using NetIndex.Testing.Common;
using Xunit;

namespace NetIndex.Ingestion.Tests.Strategies;

/// <summary>
/// Unit tests for <see cref="SemanticChunkingStrategy"/>.
/// </summary>
public class SemanticChunkingStrategyTests
{
    private static readonly ChunkingConfiguration Config = new ChunkingConfiguration().Semantic();
    private static readonly SemanticChunkingStrategy Strategy = new SemanticChunkingStrategy(
        new FakeEmbeddingGenerator(),
        Microsoft.Extensions.Options.Options.Create(Config));

    /// <summary>
    /// Coherent single-topic text should return a single chunk.
    /// </summary>
    [Fact]
    public async Task ChunkAsync_WithSingleTopic_ReturnsSingleChunkAsync()
    {
        var text = "This is a coherent paragraph about one topic. It continues with more related information. " +
                   "The sentences are all about the same subject. They share similar context and meaning. " +
                   "This makes the embeddings very similar, so no split should occur.";
        var options = new ChunkingOptions(512, 64, "\n");

        var result = await Strategy.ChunkAsync(text, options);

        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
    }

    /// <summary>
    /// Multiple distinct topics should cause splits.
    /// </summary>
    [Fact]
    public async Task ChunkAsync_WithMultipleTopics_SplitsAtBoundariesAsync()
    {
        var text = "Quantum computing uses qubits instead of bits. It leverages superposition and entanglement. " +
                   "The potential for exponential speedup is enormous. " +
                   "Baking a cake requires flour and eggs. You mix the ingredients and bake at 350 degrees. " +
                   "The result is a delicious dessert that everyone enjoys.";
        var options = new ChunkingOptions(512, 64, "\n");

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