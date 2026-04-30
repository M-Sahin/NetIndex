using NetIndex.Core.Abstractions;

namespace NetIndex.Testing.Common;

public sealed class FakeEmbeddingGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_ReturnsDeterministicVectors_ForSameInputAsync()
    {
        var generator = new FakeEmbeddingGenerator(8);

        var first = await generator.GenerateAsync("same input");
        var second = await generator.GenerateAsync("same input");

        Assert.Equal(8, first.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GenerateAsync_UsesConfiguredDimensions_AndNormalizesOutputAsync()
    {
        var generator = new FakeEmbeddingGenerator(12);

        var embedding = await generator.GenerateAsync("normalize me");
        var magnitude = Math.Sqrt(embedding.Sum(value => value * value));

        Assert.Equal(12, generator.Dimensions);
        Assert.Equal(12, embedding.Length);
        Assert.InRange(magnitude, 0.9999d, 1.0001d);
    }

    [Fact]
    public async Task GenerateBatchAsync_ReturnsDeterministicEmbeddings_InInputOrderAsync()
    {
        var generator = new FakeEmbeddingGenerator(6);

        var batch = await generator.GenerateBatchAsync(new[] { "first", "second", "first" });

        Assert.Equal(3, batch.Length);
        Assert.Equal(batch[0], batch[2]);
        Assert.NotEqual(batch[0], batch[1]);
    }
}