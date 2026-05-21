using System.ClientModel;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.AzureOpenAI.Options;
using NSubstitute;
using OpenAI.Embeddings;
using Xunit;

namespace NetIndex.Providers.AzureOpenAI.Tests.Embedding;

public sealed class AzureOpenAIEmbeddingGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_ReturnsFloatArrayAsync()
    {
        var embeddingClient = Substitute.For<EmbeddingClient>();
        embeddingClient.GenerateEmbeddingAsync("hello", Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ClientResult.FromValue(
                OpenAIEmbeddingsModelFactory.OpenAIEmbedding(0, [1.0f, 2.0f, 3.0f]),
                Substitute.For<System.ClientModel.Primitives.PipelineResponse>())));
        await using var generator = new AzureOpenAIEmbeddingGenerator(embeddingClient, 3);

        var result = await generator.GenerateAsync("hello");

        result.Should().Equal(1.0f, 2.0f, 3.0f);
    }

    [Fact]
    public async Task GenerateBatchAsync_ReturnsBatchInOrderAsync()
    {
        var embeddingClient = Substitute.For<EmbeddingClient>();
        var collection = OpenAIEmbeddingsModelFactory.OpenAIEmbeddingCollection(
            [
                OpenAIEmbeddingsModelFactory.OpenAIEmbedding(0, [1.0f, 1.0f]),
                OpenAIEmbeddingsModelFactory.OpenAIEmbedding(1, [2.0f, 2.0f]),
            ],
            "list",
            OpenAIEmbeddingsModelFactory.EmbeddingTokenUsage(2, 2));
        embeddingClient.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ClientResult.FromValue(collection, Substitute.For<System.ClientModel.Primitives.PipelineResponse>())));
        await using var generator = new AzureOpenAIEmbeddingGenerator(embeddingClient, 2);

        var result = await generator.GenerateBatchAsync(["a", "b"]);

        result.Should().HaveCount(2);
        result[0].Should().Equal(1.0f, 1.0f);
        result[1].Should().Equal(2.0f, 2.0f);
    }

    [Fact]
    public async Task GenerateAsync_WithDimensionsOption_PassesToRequestAsync()
    {
        var embeddingClient = Substitute.For<EmbeddingClient>();
        EmbeddingGenerationOptions? captured = null;
        embeddingClient.GenerateEmbeddingAsync("hello", Arg.Do<EmbeddingGenerationOptions?>(o => captured = o), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ClientResult.FromValue(
                OpenAIEmbeddingsModelFactory.OpenAIEmbedding(0, Enumerable.Repeat(1.0f, 256).ToArray()),
                Substitute.For<System.ClientModel.Primitives.PipelineResponse>())));
        await using var generator = new AzureOpenAIEmbeddingGenerator(embeddingClient, dimensions: 256, embeddingDimensions: 256);

        await generator.GenerateAsync("hello");

        captured.Should().NotBeNull();
        captured!.Dimensions.Should().Be(256);
    }

    [Fact]
    public void Dimensions_WhenUnsetAndUnknownModel_ThrowsConfigurationException()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AzureOpenAIOptions
        {
            Endpoint = new Uri("https://example.openai.azure.com/"),
            EmbeddingDeployment = "custom-embedding-deployment",
        });

        var act = () => new AzureOpenAIEmbeddingGenerator(options);

        act.Should().Throw<NetIndexConfigurationException>().WithMessage("*EmbeddingDimensions*");
    }

    [Theory]
    [InlineData("GenerateAsync")]
    [InlineData("GenerateBatchAsync")]
    public async Task PublicMethods_AfterDispose_ThrowObjectDisposedExceptionAsync(string method)
    {
        var embeddingClient = Substitute.For<EmbeddingClient>();
        var generator = new AzureOpenAIEmbeddingGenerator(embeddingClient, 3);
        await generator.DisposeAsync();

        Func<Task> act = method switch
        {
            "GenerateAsync" => () => generator.GenerateAsync("hello"),
            "GenerateBatchAsync" => () => generator.GenerateBatchAsync(["hello"]),
            _ => throw new InvalidOperationException(),
        };

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task GenerateBatchAsync_EmptyInput_ReturnsEmptyArrayAsync()
    {
        var embeddingClient = Substitute.For<EmbeddingClient>();
        await using var generator = new AzureOpenAIEmbeddingGenerator(embeddingClient, 3);

        var result = await generator.GenerateBatchAsync(Array.Empty<string>());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateAsync_DimensionMismatch_ThrowsProviderExceptionAsync()
    {
        var embeddingClient = Substitute.For<EmbeddingClient>();
        embeddingClient.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ClientResult.FromValue(
                OpenAIEmbeddingsModelFactory.OpenAIEmbedding(0, [1.0f, 2.0f]), // 2-dim vector
                Substitute.For<System.ClientModel.Primitives.PipelineResponse>())));
        await using var generator = new AzureOpenAIEmbeddingGenerator(embeddingClient, dimensions: 3); // expects 3

        var act = () => generator.GenerateAsync("hello");

        var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
        ex.Which.ErrorCode.Should().Be("dimension_mismatch");
    }

    [Fact]
    public async Task DisposeAsync_Idempotent_AfterDoubleCallAsync()
    {
        var embeddingClient = Substitute.For<EmbeddingClient>();
        var generator = new AzureOpenAIEmbeddingGenerator(embeddingClient, 3);

        await generator.DisposeAsync();
        await generator.DisposeAsync();

        true.Should().BeTrue();
    }
}
