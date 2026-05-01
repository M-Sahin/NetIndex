using Xunit;
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using NSubstitute;
using Microsoft.Extensions.DependencyInjection;

namespace NetIndex.Core.Tests;

/// <summary>
/// Pipeline contract tests for dimension mismatch validation at build time (FR11).
/// </summary>
[Trait("Category", "PipelineContract")]
public sealed class DimensionMismatchTests
{
    /// <summary>
    /// Verifies that Build() succeeds when embedding generator and vector store dimensions match.
    /// </summary>
    [Fact]
    public void Build_WithMatchingDimensions_Succeeds()
    {
        var services = new ServiceCollection();

        var mockEmbedding = Substitute.For<IEmbeddingGenerator>();
        mockEmbedding.Dimensions.Returns(384);

        var mockStore = Substitute.For<IVectorStore>();
        mockStore.Dimensions.Returns(384);

        services.AddSingleton<IEmbeddingGenerator>(mockEmbedding);
        services.AddSingleton<IVectorStore>(mockStore);

        var builder = services.AddNetIndex();

        // Should not throw
        var result = builder.Build();

        Assert.NotNull(result);
    }

    /// <summary>
    /// Verifies that Build() throws NetIndexConfigurationException when dimensions mismatch.
    /// </summary>
    [Fact]
    public void Build_WithMismatchedDimensions_ThrowsConfigurationException()
    {
        var services = new ServiceCollection();

        var mockEmbedding = Substitute.For<IEmbeddingGenerator>();
        mockEmbedding.Dimensions.Returns(384);

        var mockStore = Substitute.For<IVectorStore>();
        mockStore.Dimensions.Returns(1536);

        services.AddSingleton<IEmbeddingGenerator>(mockEmbedding);
        services.AddSingleton<IVectorStore>(mockStore);

        var builder = services.AddNetIndex();

        var exception = Assert.Throws<NetIndexConfigurationException>(() => builder.Build());

        Assert.Contains("Embedding dimension mismatch", exception.Message);
        Assert.Contains("1536", exception.Message);
        Assert.Contains("384", exception.Message);
        Assert.Equal("Dimensions", exception.PropertyName);
        Assert.Equal(1536, exception.ExpectedValue);
        Assert.Equal(384, exception.ActualValue);
    }

    /// <summary>
    /// Verifies that the exception message contains the actual interpolated dimension values.
    /// </summary>
    [Fact]
    public void Build_WithMismatchedDimensions_MessageContainsActualValues()
    {
        var services = new ServiceCollection();

        var mockEmbedding = Substitute.For<IEmbeddingGenerator>();
        mockEmbedding.Dimensions.Returns(768);

        var mockStore = Substitute.For<IVectorStore>();
        mockStore.Dimensions.Returns(1024);

        services.AddSingleton<IEmbeddingGenerator>(mockEmbedding);
        services.AddSingleton<IVectorStore>(mockStore);

        var builder = services.AddNetIndex();

        var exception = Assert.Throws<NetIndexConfigurationException>(() => builder.Build());

        Assert.Contains("expects 1024", exception.Message);
        Assert.Contains("returns 768", exception.Message);
    }

    /// <summary>
    /// Verifies that default in-memory services have matching dimensions (regression test).
    /// </summary>
    [Fact]
    public void Build_WithDefaultInMemoryServices_DimensionsMatch()
    {
        var services = new ServiceCollection();
        var builder = services.AddNetIndex();

        // Should not throw — defaults are 384/384
        var result = builder.Build();

        Assert.NotNull(result);
    }
}
