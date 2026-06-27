namespace NetIndex.Evaluation.Tests.TestSupport;

public class DeterministicTokenEmbeddingGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_SameText_ProducesIdenticalVectorAsync()
    {
        var generator = new DeterministicTokenEmbeddingGenerator(384);

        var first = await generator.GenerateAsync("the vector store ranks search results");
        var second = await generator.GenerateAsync("the vector store ranks search results");

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GenerateAsync_DifferentText_ProducesDifferentVectorAsync()
    {
        var generator = new DeterministicTokenEmbeddingGenerator(384);

        var first = await generator.GenerateAsync("vector store ranks search results");
        var second = await generator.GenerateAsync("tenant resolution enforces deny all authorization");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task GenerateAsync_ProducesUnitLengthVectorAsync()
    {
        var generator = new DeterministicTokenEmbeddingGenerator(384);

        var vector = await generator.GenerateAsync("chunking strategies divide long documents into passages");

        var normSquared = vector.Sum(v => (double)v * v);
        Assert.Equal(1.0, normSquared, precision: 3);
    }

    [Fact]
    public async Task GenerateAsync_EmptyText_ProducesZeroVectorAsync()
    {
        var generator = new DeterministicTokenEmbeddingGenerator(384);

        var vector = await generator.GenerateAsync(string.Empty);

        Assert.All(vector, value => Assert.Equal(0f, value));
    }

    [Fact]
    public async Task GenerateAsync_CancelledToken_ThrowsAsync()
    {
        var generator = new DeterministicTokenEmbeddingGenerator(384);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => generator.GenerateAsync("text", cts.Token));
    }

    [Fact]
    public async Task GenerateBatchAsync_MultipleTexts_MatchesPerTextGenerationAsync()
    {
        var generator = new DeterministicTokenEmbeddingGenerator(384);
        string[] texts = ["alpha beta", "gamma delta"];

        var batch = await generator.GenerateBatchAsync(texts);
        var individual = new[]
        {
            await generator.GenerateAsync(texts[0]),
            await generator.GenerateAsync(texts[1]),
        };

        Assert.Equal(individual[0], batch[0]);
        Assert.Equal(individual[1], batch[1]);
    }

    [Fact]
    public void Dimensions_ReturnsConfiguredValue()
    {
        var generator = new DeterministicTokenEmbeddingGenerator(384);

        Assert.Equal(384, generator.Dimensions);
    }
}
