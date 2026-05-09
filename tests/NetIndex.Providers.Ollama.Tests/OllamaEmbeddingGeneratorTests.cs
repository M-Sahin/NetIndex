using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.Ollama;
using Xunit;

namespace NetIndex.Providers.Ollama.Tests;

/// <summary>
/// Unit tests for <see cref="OllamaEmbeddingGenerator"/>.
/// </summary>
public class OllamaEmbeddingGeneratorTests
{
    private const string TestModel = "nomic-embed-text";
    private const int TestDimensions = 3;

    private static readonly string ValidEmbedJson =
        """{"embeddings":[[0.1,0.2,0.3]],"total_duration":0,"load_duration":0,"prompt_eval_count":1}""";

    /// <summary>
    /// Verifies that <see cref="OllamaEmbeddingGenerator.Dimensions"/> returns the value from configuration.
    /// </summary>
    [Fact]
    public void Dimensions_ReturnsConfiguredValue()
    {
        using var httpClient = BuildSuccessClient(ValidEmbedJson);
        var generator = new OllamaEmbeddingGenerator(httpClient, TestModel, TestDimensions);

        generator.Dimensions.Should().Be(TestDimensions);
    }

    /// <summary>
    /// Verifies that a connection-refused error results in <see cref="OllamaConnectionException"/>.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_OnConnectionRefused_ThrowsOllamaConnectionExceptionAsync()
    {
        var handler = new ExceptionThrowingHandler(new HttpRequestException("Connection refused"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        await using var generator = new OllamaEmbeddingGenerator(httpClient, TestModel, TestDimensions);

        var act = () => generator.GenerateAsync("hello");

        var ex = await act.Should().ThrowAsync<OllamaConnectionException>().WithMessage("*Ollama*");
        ex.Which.IsRetryable.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that an HTTP 500 status (via HttpRequestException) results in a retryable <see cref="NetIndexProviderException"/>.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_On500Response_ThrowsRetryableProviderExceptionAsync()
    {
        using var httpClient = BuildThrowingClient(HttpStatusCode.InternalServerError);
        await using var generator = new OllamaEmbeddingGenerator(httpClient, TestModel, TestDimensions);

        var act = () => generator.GenerateAsync("hello");

        var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
        ex.Which.IsRetryable.Should().BeTrue();
        ex.Which.ProviderName.Should().Be("Ollama");
    }

    /// <summary>
    /// Verifies that an HTTP 400 status (via HttpRequestException) results in a non-retryable <see cref="NetIndexProviderException"/>.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_On400Response_ThrowsNonRetryableProviderExceptionAsync()
    {
        using var httpClient = BuildThrowingClient(HttpStatusCode.BadRequest);
        await using var generator = new OllamaEmbeddingGenerator(httpClient, TestModel, TestDimensions);

        var act = () => generator.GenerateAsync("hello");

        var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
        ex.Which.IsRetryable.Should().BeFalse();
        ex.Which.ProviderName.Should().Be("Ollama");
    }

    /// <summary>
    /// Verifies that a pre-cancelled token causes <see cref="OperationCanceledException"/> to propagate unchanged.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_WithPreCancelledToken_ThrowsOperationCanceledExceptionAsync()
    {
        using var httpClient = BuildSuccessClient(ValidEmbedJson);
        await using var generator = new OllamaEmbeddingGenerator(httpClient, TestModel, TestDimensions);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => generator.GenerateAsync("hello", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Verifies that <see cref="OllamaEmbeddingGenerator.GenerateBatchAsync"/> returns one embedding per input
    /// and preserves input order (AC5).
    /// </summary>
    [Fact]
    public async Task GenerateBatchAsync_WithThreeTexts_ReturnsEmbeddingsInOrderAsync()
    {
        var responses = new Queue<string>(new[]
        {
            """{"embeddings":[[1.0,1.0,1.0]],"total_duration":0,"load_duration":0,"prompt_eval_count":1}""",
            """{"embeddings":[[2.0,2.0,2.0]],"total_duration":0,"load_duration":0,"prompt_eval_count":1}""",
            """{"embeddings":[[3.0,3.0,3.0]],"total_duration":0,"load_duration":0,"prompt_eval_count":1}""",
        });
        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses.Dequeue(), Encoding.UTF8, "application/json"),
            }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        await using var generator = new OllamaEmbeddingGenerator(httpClient, TestModel, TestDimensions);

        var result = await generator.GenerateBatchAsync(["text1", "text2", "text3"]);

        result.Should().HaveCount(3);
        result[0].Should().BeEquivalentTo(new[] { 1.0f, 1.0f, 1.0f });
        result[1].Should().BeEquivalentTo(new[] { 2.0f, 2.0f, 2.0f });
        result[2].Should().BeEquivalentTo(new[] { 3.0f, 3.0f, 3.0f });
    }

    /// <summary>
    /// Verifies that passing null text throws <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_WithNullText_ThrowsArgumentNullExceptionAsync()
    {
        using var httpClient = BuildSuccessClient(ValidEmbedJson);
        await using var generator = new OllamaEmbeddingGenerator(httpClient, TestModel, TestDimensions);

        var act = () => generator.GenerateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// Verifies that an HTTP 5xx response (parsed by OllamaSharp 5.x into <c>OllamaException</c>)
    /// is wrapped in a retryable <see cref="NetIndexProviderException"/>. Covers the OllamaSharp 5.x code path.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_OnOllamaServerErrorResponse_ThrowsRetryableProviderExceptionAsync()
    {
        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("""{"error":"internal server error"}""", Encoding.UTF8, "application/json"),
            }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        await using var generator = new OllamaEmbeddingGenerator(httpClient, TestModel, TestDimensions);

        var act = () => generator.GenerateAsync("test");

        var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
        ex.Which.IsRetryable.Should().BeTrue();
        ex.Which.ProviderName.Should().Be("Ollama");
    }

    /// <summary>
    /// Verifies that an HTTP 429 (Too Many Requests) is wrapped as retryable per the framework contract.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_On429Response_ThrowsRetryableProviderExceptionAsync()
    {
        using var httpClient = BuildThrowingClient(HttpStatusCode.TooManyRequests);
        await using var generator = new OllamaEmbeddingGenerator(httpClient, TestModel, TestDimensions);

        var act = () => generator.GenerateAsync("hello");

        var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
        ex.Which.IsRetryable.Should().BeTrue();
        ex.Which.HttpStatusCode.Should().Be(429);
        ex.Which.ErrorCode.Should().Be("rate_limited");
    }

    /// <summary>
    /// Verifies that an <see cref="IOException"/> from the transport is wrapped as <see cref="OllamaConnectionException"/> (NFR9).
    /// </summary>
    [Fact]
    public async Task GenerateAsync_OnIOException_ThrowsOllamaConnectionExceptionAsync()
    {
        var handler = new ExceptionThrowingHandler(new IOException("stream error"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        await using var generator = new OllamaEmbeddingGenerator(httpClient, TestModel, TestDimensions);

        var act = () => generator.GenerateAsync("test");

        await act.Should().ThrowAsync<OllamaConnectionException>();
    }

    /// <summary>
    /// Verifies that a <see cref="SocketException"/> from the transport is wrapped as <see cref="OllamaConnectionException"/> (NFR9).
    /// </summary>
    [Fact]
    public async Task GenerateAsync_OnSocketException_ThrowsOllamaConnectionExceptionAsync()
    {
        var handler = new ExceptionThrowingHandler(new SocketException((int)SocketError.ConnectionRefused));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        await using var generator = new OllamaEmbeddingGenerator(httpClient, TestModel, TestDimensions);

        var act = () => generator.GenerateAsync("test");

        await act.Should().ThrowAsync<OllamaConnectionException>();
    }

    /// <summary>
    /// Verifies that <see cref="OllamaEmbeddingGenerator.GenerateBatchAsync"/> propagates a pre-cancelled token.
    /// </summary>
    [Fact]
    public async Task GenerateBatchAsync_WithPreCancelledToken_ThrowsOperationCanceledExceptionAsync()
    {
        using var httpClient = BuildSuccessClient(ValidEmbedJson);
        await using var generator = new OllamaEmbeddingGenerator(httpClient, TestModel, TestDimensions);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => generator.GenerateBatchAsync(["text1", "text2"], cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Verifies that calling <see cref="OllamaEmbeddingGenerator.GenerateAsync(string, CancellationToken)"/>
    /// after disposal throws <see cref="ObjectDisposedException"/> (NFR9 — no raw upstream exceptions).
    /// </summary>
    [Fact]
    public async Task GenerateAsync_AfterDispose_ThrowsObjectDisposedExceptionAsync()
    {
        using var httpClient = BuildSuccessClient(ValidEmbedJson);
        var generator = new OllamaEmbeddingGenerator(httpClient, TestModel, TestDimensions);
        await generator.DisposeAsync();

        var act = () => generator.GenerateAsync("hello");

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    private static HttpClient BuildSuccessClient(string responseBody)
    {
        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            }));
        return new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
    }

    private static HttpClient BuildThrowingClient(HttpStatusCode statusCode)
    {
        var handler = new ThrowingHttpMessageHandler(statusCode);
        return new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
    }
}

internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    internal MockHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        => _handler = handler;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => _handler(request, cancellationToken);
}

internal sealed class ThrowingHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;

    internal ThrowingHttpMessageHandler(HttpStatusCode statusCode)
        => _statusCode = statusCode;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => throw new HttpRequestException($"Simulated HTTP {(int)_statusCode}", null, _statusCode);
}

internal sealed class ExceptionThrowingHandler : HttpMessageHandler
{
    private readonly Exception _exception;

    internal ExceptionThrowingHandler(Exception exception)
        => _exception = exception;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => throw _exception;
}
