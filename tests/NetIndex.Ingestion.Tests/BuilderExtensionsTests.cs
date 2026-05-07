using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Options;
using NetIndex.Ingestion.Strategies;
using Xunit;
using Opts = Microsoft.Extensions.Options.Options;

namespace NetIndex.Ingestion.Tests;

/// <summary>
/// Unit tests for <see cref="NetIndexBuilderExtensions"/>.
/// </summary>
public class BuilderExtensionsTests
{
    /// <summary>
    /// UseChunking with FixedSize should register FixedSizeChunkingStrategy.
    /// </summary>
    [Fact]
    public void UseChunking_WithFixedSize_RegistersFixedSizeStrategy()
    {
        var services = new ServiceCollection();
        var builder = new TestNetIndexBuilder(services);

        builder.UseChunking(c => c.FixedSize(512, 64));
        var provider = services.BuildServiceProvider();

        var strategy = provider.GetRequiredService<IChunkingStrategy>();
        strategy.Should().BeOfType<FixedSizeChunkingStrategy>();
    }

    /// <summary>
    /// UseChunking with Semantic should register SemanticChunkingStrategy.
    /// </summary>
    [Fact]
    public void UseChunking_WithSemantic_RegistersSemanticStrategy()
    {
        var services = new ServiceCollection();
        var builder = new TestNetIndexBuilder(services);

        // Semantic needs IEmbeddingGenerator
        services.AddSingleton<IEmbeddingGenerator>(new Testing.Common.FakeEmbeddingGenerator());

        builder.UseChunking(c => c.Semantic());
        var provider = services.BuildServiceProvider();

        var strategy = provider.GetRequiredService<IChunkingStrategy>();
        strategy.Should().BeOfType<SemanticChunkingStrategy>();
    }

    /// <summary>
    /// UseChunking with Recursive should register RecursiveChunkingStrategy.
    /// </summary>
    [Fact]
    public void UseChunking_WithRecursive_RegistersRecursiveStrategy()
    {
        var services = new ServiceCollection();
        var builder = new TestNetIndexBuilder(services);

        // Recursive needs IEmbeddingGenerator
        services.AddSingleton<IEmbeddingGenerator>(new Testing.Common.FakeEmbeddingGenerator());

        builder.UseChunking(c => c.Recursive());
        var provider = services.BuildServiceProvider();

        var strategy = provider.GetRequiredService<IChunkingStrategy>();
        strategy.Should().BeOfType<RecursiveChunkingStrategy>();
    }

    /// <summary>
    /// UseChunking with null builder should throw ArgumentNullException.
    /// </summary>
    [Fact]
    public void UseChunking_WithNullBuilder_ThrowsArgumentNullException()
    {
        INetIndexBuilder? builder = null;

        var act = () => builder!.UseChunking(c => c.FixedSize(512, 64));

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// UseChunking with null configure should throw ArgumentNullException.
    /// </summary>
    [Fact]
    public void UseChunking_WithNullConfigure_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var builder = new TestNetIndexBuilder(services);

        var act = () => builder.UseChunking(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Test implementation of INetIndexBuilder for DI testing.
    /// </summary>
    private sealed class TestNetIndexBuilder : INetIndexBuilder
    {
        public IServiceCollection Services { get; }

        public TestNetIndexBuilder(IServiceCollection services)
        {
            Services = services;
        }

        /// <summary>
        /// Not used in tests — returns null.
        /// </summary>
        public object Build() => null!;
    }
}