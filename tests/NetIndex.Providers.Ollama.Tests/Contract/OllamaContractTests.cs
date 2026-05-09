using System.Net;
using System.Text;
using FluentAssertions;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.Ollama;
using Xunit;

namespace NetIndex.Providers.Ollama.Tests.Contract;

/// <summary>
/// Contract tests verifying <see cref="OllamaEmbeddingGenerator"/> exception wrapping (NFR9).
/// </summary>
[Trait("Category", "ContractTest")]
[Trait("Category", "PipelineContract")]
public class OllamaContractTests
{
    private const string TestModel = "nomic-embed-text";
    private const int TestDimensions = 3;

    /// <summary>
    /// Verifies that an HTTP 5xx status is wrapped in a retryable <see cref="NetIndexProviderException"/>.
    /// </summary>
    [Fact]
    public async Task WrapProviderException_On5xxResponse_ThrowsNetIndexProviderExceptionAsync()
    {
        using var httpClient = BuildThrowingClient(HttpStatusCode.InternalServerError);
        await using var generator = new OllamaEmbeddingGenerator(httpClient, TestModel, TestDimensions);

        var act = () => generator.GenerateAsync("test");

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
        await using var generator = new OllamaEmbeddingGenerator(httpClient, TestModel, TestDimensions);

        var act = () => generator.GenerateAsync("test");

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
        await using var generator = new OllamaEmbeddingGenerator(httpClient, TestModel, TestDimensions);

        var act = () => generator.GenerateAsync("test");

        var ex = await act.Should().ThrowAsync<OllamaConnectionException>();
        ex.Which.IsRetryable.Should().BeTrue();
    }

    private static HttpClient BuildThrowingClient(HttpStatusCode statusCode)
    {
        var handler = new ContractThrowingHttpMessageHandler(statusCode);
        return new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
    }
}

internal sealed class ContractThrowingHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;

    internal ContractThrowingHttpMessageHandler(HttpStatusCode statusCode)
        => _statusCode = statusCode;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => throw new HttpRequestException($"Simulated HTTP {(int)_statusCode}", null, _statusCode);
}

internal sealed class ContractExceptionThrowingHandler : HttpMessageHandler
{
    private readonly Exception _exception;

    internal ContractExceptionThrowingHandler(Exception exception)
        => _exception = exception;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => throw _exception;
}
