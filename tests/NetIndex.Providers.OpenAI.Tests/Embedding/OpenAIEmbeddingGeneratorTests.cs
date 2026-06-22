using System.ClientModel;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.OpenAI.Options;
using NSubstitute;
using OpenAI.Embeddings;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace NetIndex.Providers.OpenAI.Tests.Embedding;

public sealed class OpenAIEmbeddingGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_ReturnsFloatArrayAsync()
    {
        var client = Substitute.For<EmbeddingClient>();
        client.GenerateEmbeddingAsync("hello", Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ClientResult.FromValue(
                OpenAIEmbeddingsModelFactory.OpenAIEmbedding(0, [1.0f, 2.0f, 3.0f]),
                Substitute.For<System.ClientModel.Primitives.PipelineResponse>())));
        await using var generator = new OpenAIEmbeddingGenerator(client, 3);

        var result = await generator.GenerateAsync("hello");

        result.Should().Equal(1.0f, 2.0f, 3.0f);
    }

    [Fact]
    public async Task GenerateBatchAsync_ReturnsBatchInOrderAsync()
    {
        var client = Substitute.For<EmbeddingClient>();
        var collection = OpenAIEmbeddingsModelFactory.OpenAIEmbeddingCollection(
            [
                OpenAIEmbeddingsModelFactory.OpenAIEmbedding(0, [1.0f, 1.0f]),
                OpenAIEmbeddingsModelFactory.OpenAIEmbedding(1, [2.0f, 2.0f]),
            ],
            "list",
            OpenAIEmbeddingsModelFactory.EmbeddingTokenUsage(2, 2));
        client.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ClientResult.FromValue(collection, Substitute.For<System.ClientModel.Primitives.PipelineResponse>())));
        await using var generator = new OpenAIEmbeddingGenerator(client, 2);

        var result = await generator.GenerateBatchAsync(["a", "b"]);

        result.Should().HaveCount(2);
        result[0].Should().Equal(1.0f, 1.0f);
        result[1].Should().Equal(2.0f, 2.0f);
    }

    [Fact]
    public async Task GenerateAsync_WithDimensionsOption_PassesToRequestAsync()
    {
        var client = Substitute.For<EmbeddingClient>();
        EmbeddingGenerationOptions? captured = null;
        client.GenerateEmbeddingAsync("hello", Arg.Do<EmbeddingGenerationOptions?>(o => captured = o), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ClientResult.FromValue(
                OpenAIEmbeddingsModelFactory.OpenAIEmbedding(0, Enumerable.Repeat(1.0f, 256).ToArray()),
                Substitute.For<System.ClientModel.Primitives.PipelineResponse>())));
        await using var generator = new OpenAIEmbeddingGenerator(client, dimensions: 256, embeddingDimensions: 256);

        await generator.GenerateAsync("hello");

        captured.Should().NotBeNull();
        captured!.Dimensions.Should().Be(256);
    }

    [Fact]
    public async Task GenerateBatchAsync_EmptyInput_ReturnsEmptyArrayAsync()
    {
        var client = Substitute.For<EmbeddingClient>();
        await using var generator = new OpenAIEmbeddingGenerator(client, 3);

        var result = await generator.GenerateBatchAsync(Array.Empty<string>());

        result.Should().BeEmpty();
        await client.DidNotReceive().GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateAsync_DimensionMismatch_ThrowsProviderExceptionAsync()
    {
        var client = Substitute.For<EmbeddingClient>();
        client.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ClientResult.FromValue(
                OpenAIEmbeddingsModelFactory.OpenAIEmbedding(0, [1.0f, 2.0f]),
                Substitute.For<System.ClientModel.Primitives.PipelineResponse>())));
        await using var generator = new OpenAIEmbeddingGenerator(client, dimensions: 3);

        var act = () => generator.GenerateAsync("hello");

        var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
        ex.Which.ErrorCode.Should().Be("dimension_mismatch");
        ex.Which.ProviderName.Should().Be("OpenAI");
    }

    [Fact]
    public void Dimensions_UnknownModel_ThrowsConfigurationException()
    {
        var options = MsOptions.Create(new OpenAIOptions
        {
            ApiKey = "sk-test",
            EmbeddingModel = "unknown-model-xyz",
        });

        var act = () => new OpenAIEmbeddingGenerator(options);

        act.Should().Throw<NetIndexConfigurationException>().WithMessage("*EmbeddingDimensions*");
    }

    [Theory]
    [InlineData("text-embedding-3-small", 1536)]
    [InlineData("text-embedding-3-large", 3072)]
    [InlineData("text-embedding-ada-002", 1536)]
    public void Dimensions_KnownModel_InferredCorrectly(string model, int expectedDims)
    {
        var dims = OpenAIEmbeddingModels.ResolveDimensions(model, null);
        dims.Should().Be(expectedDims);
    }

    [Fact]
    public async Task GenerateAsync_CancellationToken_PropagatedToSdkAsync()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var client = Substitute.For<EmbeddingClient>();
        client.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<EmbeddingGenerationOptions?>(), cts.Token)
            .Returns<Task<ClientResult<OpenAIEmbedding>>>(_ => throw new OperationCanceledException(cts.Token));
        await using var generator = new OpenAIEmbeddingGenerator(client, 3);

        var act = () => generator.GenerateAsync("hello", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("GenerateAsync")]
    [InlineData("GenerateBatchAsync")]
    public async Task PublicMethods_AfterDispose_ThrowObjectDisposedExceptionAsync(string method)
    {
        var client = Substitute.For<EmbeddingClient>();
        var generator = new OpenAIEmbeddingGenerator(client, 3);
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
    public async Task DisposeAsync_Idempotent_AfterDoubleCallAsync()
    {
        var client = Substitute.For<EmbeddingClient>();
        var generator = new OpenAIEmbeddingGenerator(client, 3);

        await generator.DisposeAsync();
        await generator.DisposeAsync();

        true.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateBatchAsync_PreCanceledToken_EmptyInput_ThrowsOperationCanceledAsync()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var client = Substitute.For<EmbeddingClient>();
        await using var generator = new OpenAIEmbeddingGenerator(client, 3);

        var act = () => generator.GenerateBatchAsync([], cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await client.DidNotReceive().GenerateEmbeddingsAsync(
            Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateBatchAsync_CountMismatch_ThrowsProviderExceptionAsync()
    {
        var client = Substitute.For<EmbeddingClient>();
        var collection = OpenAIEmbeddingsModelFactory.OpenAIEmbeddingCollection(
            [OpenAIEmbeddingsModelFactory.OpenAIEmbedding(0, [1.0f, 2.0f])],
            "list",
            OpenAIEmbeddingsModelFactory.EmbeddingTokenUsage(1, 1));
        client.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ClientResult.FromValue(collection, Substitute.For<System.ClientModel.Primitives.PipelineResponse>())));
        await using var generator = new OpenAIEmbeddingGenerator(client, 2);

        var act = () => generator.GenerateBatchAsync(["a", "b"]);

        var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
        ex.Which.ErrorCode.Should().Be("invalid_response");
        ex.Which.IsRetryable.Should().BeFalse();
        ex.Which.ProviderName.Should().Be("OpenAI");
    }

    [Fact]
    public async Task GenerateBatchAsync_NonContiguousIndices_ThrowsProviderExceptionAsync()
    {
        // Count matches input (2), but indices are {0, 0} instead of {0, 1}: a duplicate paired
        // with a missing index that OrderBy would otherwise silently mis-align.
        var client = Substitute.For<EmbeddingClient>();
        var collection = OpenAIEmbeddingsModelFactory.OpenAIEmbeddingCollection(
            [
                OpenAIEmbeddingsModelFactory.OpenAIEmbedding(0, [1.0f, 1.0f]),
                OpenAIEmbeddingsModelFactory.OpenAIEmbedding(0, [2.0f, 2.0f]),
            ],
            "list",
            OpenAIEmbeddingsModelFactory.EmbeddingTokenUsage(2, 2));
        client.GenerateEmbeddingsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<EmbeddingGenerationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ClientResult.FromValue(collection, Substitute.For<System.ClientModel.Primitives.PipelineResponse>())));
        await using var generator = new OpenAIEmbeddingGenerator(client, 2);

        var act = () => generator.GenerateBatchAsync(["a", "b"]);

        var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
        ex.Which.ErrorCode.Should().Be("invalid_response");
        ex.Which.IsRetryable.Should().BeFalse();
        ex.Which.ProviderName.Should().Be("OpenAI");
    }

    [Theory]
    [InlineData("text-embedding-3-small", 1537)]
    [InlineData("text-embedding-3-large", 3073)]
    [InlineData("text-embedding-ada-002", 2000)]
    public void ResolveDimensions_KnownModelWithExcessiveDimensions_ThrowsConfigurationException(string model, int excessiveDims)
    {
        var act = () => OpenAIEmbeddingModels.ResolveDimensions(model, excessiveDims);

        act.Should().Throw<NetIndexConfigurationException>().WithMessage($"*{excessiveDims}*");
    }
}
