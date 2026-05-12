using System.Net;
using FluentAssertions;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.Ollama;
using Xunit;

namespace NetIndex.Providers.Ollama.Tests.Contract;

/// <summary>
/// Contract tests verifying <see cref="OllamaChatClient"/> exception wrapping (NFR9).
/// </summary>
[Trait("Category", "ContractTest")]
[Trait("Category", "PipelineContract")]
public class OllamaChatClientContractTests
{
    private const string TestModel = "llama3.2";

    /// <summary>
    /// Verifies that an HTTP 5xx status is wrapped in a retryable <see cref="NetIndexProviderException"/>.
    /// </summary>
    [Fact]
    public async Task WrapProviderException_On5xxResponse_ThrowsNetIndexProviderExceptionAsync()
    {
        using var httpClient = BuildThrowingClient(HttpStatusCode.InternalServerError);
        await using var client = new OllamaChatClient(httpClient, TestModel);

        var act = async () =>
        {
            await foreach (var _ in client.GenerateStreamingAsync("test", Array.Empty<RagChunk>()))
            {
            }
        };

        var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
        ex.Which.IsRetryable.Should().BeTrue();
        ex.Which.ProviderName.Should().Be("Ollama");
        ex.Which.Should().NotBeOfType<OllamaConnectionException>();
    }

    /// <summary>
    /// Verifies that an HTTP 4xx status is wrapped in a non-retryable <see cref="NetIndexProviderException"/>.
    /// </summary>
    [Fact]
    public async Task WrapProviderException_On4xxResponse_ThrowsNonRetryableNetIndexProviderExceptionAsync()
    {
        using var httpClient = BuildThrowingClient(HttpStatusCode.BadRequest);
        await using var client = new OllamaChatClient(httpClient, TestModel);

        var act = async () =>
        {
            await foreach (var _ in client.GenerateStreamingAsync("test", Array.Empty<RagChunk>()))
            {
            }
        };

        var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
        ex.Which.IsRetryable.Should().BeFalse();
        ex.Which.ProviderName.Should().Be("Ollama");
    }

    /// <summary>
    /// Verifies that a connection-level failure is wrapped in <see cref="OllamaConnectionException"/> with <c>IsRetryable = true</c>.
    /// </summary>
    [Fact]
    public async Task WrapProviderException_OnConnectionFailure_ThrowsOllamaConnectionExceptionAsync()
    {
        var handler = new ContractExceptionThrowingHandler(new HttpRequestException("Connection refused"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        await using var client = new OllamaChatClient(httpClient, TestModel);

        var act = async () =>
        {
            await foreach (var _ in client.GenerateStreamingAsync("test", Array.Empty<RagChunk>()))
            {
            }
        };

        var ex = await act.Should().ThrowAsync<OllamaConnectionException>();
        ex.Which.IsRetryable.Should().BeTrue();
    }

    private static HttpClient BuildThrowingClient(HttpStatusCode statusCode)
    {
        var handler = new ContractThrowingHttpMessageHandler(statusCode);
        return new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
    }
}
