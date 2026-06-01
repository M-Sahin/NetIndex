using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.Ollama;
using Xunit;

namespace NetIndex.Providers.Ollama.Tests;

/// <summary>
/// Unit tests for <see cref="OllamaChatClient"/>.
/// </summary>
public class OllamaChatClientTests
{
    private const string TestModel = "llama3.2";

    private static readonly string ThreeChunkBody =
        "{\"model\":\"llama3.2\",\"created_at\":\"2026-05-12T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":\"Hello\"},\"done\":false}\n" +
        "{\"model\":\"llama3.2\",\"created_at\":\"2026-05-12T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":\" world\"},\"done\":false}\n" +
        "{\"model\":\"llama3.2\",\"created_at\":\"2026-05-12T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":\"!\"},\"done\":true,\"done_reason\":\"stop\",\"total_duration\":1,\"load_duration\":1,\"prompt_eval_count\":1,\"eval_count\":3}\n";

    private static RagChunk MakeChunk(string text) =>
        new("id1", text, null, "doc1", null);

    private static HttpClient BuildStreamingClient(string body)
    {
        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/x-ndjson"),
            }));
        return new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
    }

    private static HttpClient BuildThrowingClient(HttpStatusCode statusCode)
    {
        var handler = new ThrowingHttpMessageHandler(statusCode);
        return new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
    }

    private static async Task<List<GenerationChunk>> CollectAsync(
        IAsyncEnumerable<GenerationChunk> stream)
    {
        var result = new List<GenerationChunk>();
        await foreach (var chunk in stream)
        {
            result.Add(chunk);
        }
        return result;
    }

    // ── Constructor guards ───────────────────────────────────────────────────

    /// <summary>
    /// Verifies that the production constructor throws when options is null.
    /// </summary>
    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        var act = () => new OllamaChatClient(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Verifies that the internal test constructor throws when httpClient is null.
    /// </summary>
    [Fact]
    public void InternalConstructor_WithNullHttpClient_ThrowsArgumentNullException()
    {
        var act = () => new OllamaChatClient(null!, TestModel);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Verifies that the internal test constructor throws when model is null.
    /// </summary>
    [Fact]
    public void InternalConstructor_WithNullModel_ThrowsArgumentNullException()
    {
        using var httpClient = BuildStreamingClient(ThreeChunkBody);

        var act = () => new OllamaChatClient(httpClient, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── Streaming happy paths ────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a 3-chunk response yields all three chunks in order (AC #3).
    /// </summary>
    [Fact]
    public async Task GenerateStreamingAsync_WithThreeChunkResponse_YieldsAllThreeInOrderAsync()
    {
        using var httpClient = BuildStreamingClient(ThreeChunkBody);
        await using var client = new OllamaChatClient(httpClient, TestModel);

        var chunks = await CollectAsync(client.GenerateStreamingAsync("test", []));

        chunks.Should().HaveCount(3);
        chunks[0].Text.Should().Be("Hello");
        chunks[0].IsComplete.Should().BeFalse();
        chunks[1].Text.Should().Be(" world");
        chunks[1].IsComplete.Should().BeFalse();
        chunks[2].Text.Should().Be("!");
        chunks[2].IsComplete.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that the final chunk with <c>done_reason:"stop"</c> maps to <see cref="FinishReason.Stop"/> (AC #3).
    /// </summary>
    [Fact]
    public async Task GenerateStreamingAsync_FinalChunk_HasFinishReasonStopAsync()
    {
        var body = "{\"model\":\"llama3.2\",\"created_at\":\"2026-05-12T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":\"token\"},\"done\":true,\"done_reason\":\"stop\",\"total_duration\":1,\"load_duration\":1,\"prompt_eval_count\":1,\"eval_count\":1}\n";
        using var httpClient = BuildStreamingClient(body);
        await using var client = new OllamaChatClient(httpClient, TestModel);

        var chunks = await CollectAsync(client.GenerateStreamingAsync("test", []));

        chunks.Should().HaveCount(1);
        chunks[0].FinishReason.Should().Be(FinishReason.Stop);
    }

    /// <summary>
    /// Verifies that <c>done_reason:"length"</c> maps to <see cref="FinishReason.Length"/> (AC #3).
    /// </summary>
    [Fact]
    public async Task GenerateStreamingAsync_FinalChunk_WithLengthDoneReason_HasFinishReasonLengthAsync()
    {
        var body = "{\"model\":\"llama3.2\",\"created_at\":\"2026-05-12T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":\"token\"},\"done\":true,\"done_reason\":\"length\",\"total_duration\":1,\"load_duration\":1,\"prompt_eval_count\":1,\"eval_count\":1}\n";
        using var httpClient = BuildStreamingClient(body);
        await using var client = new OllamaChatClient(httpClient, TestModel);

        var chunks = await CollectAsync(client.GenerateStreamingAsync("test", []));

        chunks.Should().HaveCount(1);
        chunks[0].FinishReason.Should().Be(FinishReason.Length);
    }

    /// <summary>
    /// Verifies that an unknown <c>done_reason</c> defaults to <see cref="FinishReason.Stop"/> (AC #3).
    /// </summary>
    [Fact]
    public async Task GenerateStreamingAsync_FinalChunk_WithUnknownDoneReason_DefaultsToStopAsync()
    {
        var body = "{\"model\":\"llama3.2\",\"created_at\":\"2026-05-12T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":\"token\"},\"done\":true,\"done_reason\":\"foo\",\"total_duration\":1,\"load_duration\":1,\"prompt_eval_count\":1,\"eval_count\":1}\n";
        using var httpClient = BuildStreamingClient(body);
        await using var client = new OllamaChatClient(httpClient, TestModel);

        var chunks = await CollectAsync(client.GenerateStreamingAsync("test", []));

        chunks.Should().HaveCount(1);
        chunks[0].FinishReason.Should().Be(FinishReason.Stop);
    }

    // ── Non-streaming (GenerateAsync) ────────────────────────────────────────

    /// <summary>
    /// Verifies that <see cref="OllamaChatClient.GenerateAsync"/> concatenates all chunk texts (AC #4).
    /// </summary>
    [Fact]
    public async Task GenerateAsync_WithMultiChunkStream_ReturnsConcatenatedTextAsync()
    {
        using var httpClient = BuildStreamingClient(ThreeChunkBody);
        await using var client = new OllamaChatClient(httpClient, TestModel);

        var result = await client.GenerateAsync("test", []);

        result.Should().Be("Hello world!");
    }

    // ── Request body assembly ────────────────────────────────────────────────

    /// <summary>
    /// Verifies that an empty context sends a user message containing only the prompt — no "Context:" prefix (AC #2).
    /// </summary>
    [Fact]
    public async Task GenerateStreamingAsync_WithEmptyContext_StillSendsRequestAsync()
    {
        string? capturedBody = null;
        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            if (req.Content is not null)
            {
                capturedBody = await req.Content.ReadAsStringAsync(ct);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ThreeChunkBody, Encoding.UTF8, "application/x-ndjson"),
            };
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        await using var client = new OllamaChatClient(httpClient, TestModel);

        await CollectAsync(client.GenerateStreamingAsync("What is X?", []));

        capturedBody.Should().NotBeNull();
        capturedBody!.Should().Contain("What is X?");
        capturedBody.Should().NotContain("Context:");
    }

    /// <summary>
    /// Verifies that context chunks are assembled into a "Context:\n...\n\nQuestion: {prompt}" user message (AC #2).
    /// </summary>
    [Fact]
    public async Task GenerateStreamingAsync_WithContext_AssemblesContextPrefixedUserMessageAsync()
    {
        string? capturedBody = null;
        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            if (req.Content is not null)
            {
                capturedBody = await req.Content.ReadAsStringAsync(ct);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ThreeChunkBody, Encoding.UTF8, "application/x-ndjson"),
            };
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        await using var client = new OllamaChatClient(httpClient, TestModel);
        var context = new[] { MakeChunk("First fact."), MakeChunk("Second fact.") };

        await CollectAsync(client.GenerateStreamingAsync("What is Y?", context));

        capturedBody.Should().NotBeNull();
        capturedBody!.Should().Contain("Context:");
        capturedBody.Should().Contain("First fact.");
        capturedBody.Should().Contain("Second fact.");
        capturedBody.Should().Contain("---");
        capturedBody.Should().Contain("Question: What is Y?");
    }

    // ── Empty response ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that an empty stream (zero chunks) throws <see cref="NetIndexProviderException"/> with <c>empty_response</c> code.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_WithEmptyResponse_ThrowsNetIndexProviderExceptionAsync()
    {
        var body = "{\"model\":\"llama3.2\",\"created_at\":\"2026-05-12T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":\"\"},\"done\":true,\"done_reason\":\"stop\",\"total_duration\":1,\"load_duration\":1,\"prompt_eval_count\":1,\"eval_count\":1}\n";
        using var httpClient = BuildStreamingClient(body);
        await using var client = new OllamaChatClient(httpClient, TestModel);

        var act = () => client.GenerateAsync("test", []);

        var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
        ex.Which.ErrorCode.Should().Be("empty_response");
        ex.Which.IsRetryable.Should().BeFalse();
    }

    // ── Context with null elements ─────────────────────────────────────────────

    /// <summary>
    /// Verifies that a null <see cref="RagChunk"/> element in the context enumerable does not throw NRE (NFR9).
    /// </summary>
    [Fact]
    public async Task GenerateStreamingAsync_WithNullContextElement_DoesNotThrowNullReferenceExceptionAsync()
    {
        using var httpClient = BuildStreamingClient(ThreeChunkBody);
        await using var client = new OllamaChatClient(httpClient, TestModel);
        var context = new RagChunk?[] { MakeChunk("First"), null, MakeChunk("Second") };

        var act = async () => await CollectAsync(client.GenerateStreamingAsync("test", context!));

        await act.Should().NotThrowAsync<NullReferenceException>();
    }

    // ── Mid-stream failure ─────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a failure after the first chunk (mid-stream) is properly wrapped.
    /// Uses a custom stream that writes the first chunk then throws on the next read.
    /// </summary>
    [Fact]
    public async Task GenerateStreamingAsync_MidStreamFailure_ThrowsWrappedExceptionAsync()
    {
        var firstChunkBytes = Encoding.UTF8.GetBytes(
            "{\"model\":\"llama3.2\",\"created_at\":\"2026-05-12T00:00:00Z\",\"message\":{\"role\":\"assistant\",\"content\":\"Hello\"},\"done\":false}\n");
        var stream = new MidStreamThrowingStream(firstChunkBytes, new IOException("Connection reset"));
        var handler = new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream),
            }));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        await using var client = new OllamaChatClient(httpClient, TestModel);

        var act = async () =>
        {
            var chunks = new List<GenerationChunk>();
            await foreach (var chunk in client.GenerateStreamingAsync("test", []))
            {
                chunks.Add(chunk);
            }
        };

        var ex = await act.Should().ThrowAsync<OllamaConnectionException>();
        ex.Which.IsRetryable.Should().BeTrue();
    }

    // ── Exception wrapping: HTTP errors ──────────────────────────────────────

    /// <summary>
    /// Verifies that an HTTP 500 error yields a retryable <see cref="NetIndexProviderException"/> (AC #5).
    /// </summary>
    [Fact]
    public async Task GenerateStreamingAsync_On500Response_ThrowsRetryableProviderExceptionAsync()
    {
        using var httpClient = BuildThrowingClient(HttpStatusCode.InternalServerError);
        await using var client = new OllamaChatClient(httpClient, TestModel);

        var act = async () => await CollectAsync(client.GenerateStreamingAsync("test", []));

        var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
        ex.Which.IsRetryable.Should().BeTrue();
        ex.Which.ProviderName.Should().Be("Ollama");
    }

    /// <summary>
    /// Verifies that an HTTP 429 error yields a retryable <see cref="NetIndexProviderException"/> with rate-limit code (AC #5).
    /// </summary>
    [Fact]
    public async Task GenerateStreamingAsync_On429Response_ThrowsRetryableProviderExceptionAsync()
    {
        using var httpClient = BuildThrowingClient(HttpStatusCode.TooManyRequests);
        await using var client = new OllamaChatClient(httpClient, TestModel);

        var act = async () => await CollectAsync(client.GenerateStreamingAsync("test", []));

        var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
        ex.Which.IsRetryable.Should().BeTrue();
        ex.Which.ErrorCode.Should().Be("rate_limited");
    }

    /// <summary>
    /// Verifies that an HTTP 400 error yields a non-retryable <see cref="NetIndexProviderException"/> (AC #6).
    /// </summary>
    [Fact]
    public async Task GenerateStreamingAsync_On400Response_ThrowsNonRetryableProviderExceptionAsync()
    {
        using var httpClient = BuildThrowingClient(HttpStatusCode.BadRequest);
        await using var client = new OllamaChatClient(httpClient, TestModel);

        var act = async () => await CollectAsync(client.GenerateStreamingAsync("test", []));

        var ex = await act.Should().ThrowAsync<NetIndexProviderException>();
        ex.Which.IsRetryable.Should().BeFalse();
        ex.Which.ProviderName.Should().Be("Ollama");
    }

    // ── Exception wrapping: connection failures ───────────────────────────────

    /// <summary>
    /// Verifies that a connection-refused <see cref="HttpRequestException"/> is wrapped in <see cref="OllamaConnectionException"/> (AC #7).
    /// </summary>
    [Fact]
    public async Task GenerateStreamingAsync_OnConnectionRefused_ThrowsOllamaConnectionExceptionAsync()
    {
        var handler = new ExceptionThrowingHandler(new HttpRequestException("Connection refused"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        await using var client = new OllamaChatClient(httpClient, TestModel);

        var act = async () => await CollectAsync(client.GenerateStreamingAsync("test", []));

        var ex = await act.Should().ThrowAsync<OllamaConnectionException>();
        ex.Which.IsRetryable.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that an <see cref="IOException"/> is wrapped in <see cref="OllamaConnectionException"/> (AC #7).
    /// </summary>
    [Fact]
    public async Task GenerateStreamingAsync_OnIOException_ThrowsOllamaConnectionExceptionAsync()
    {
        var handler = new ExceptionThrowingHandler(new IOException("stream error"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        await using var client = new OllamaChatClient(httpClient, TestModel);

        var act = async () => await CollectAsync(client.GenerateStreamingAsync("test", []));

        await act.Should().ThrowAsync<OllamaConnectionException>();
    }

    /// <summary>
    /// Verifies that a <see cref="SocketException"/> is wrapped in <see cref="OllamaConnectionException"/> (AC #7).
    /// </summary>
    [Fact]
    public async Task GenerateStreamingAsync_OnSocketException_ThrowsOllamaConnectionExceptionAsync()
    {
        var handler = new ExceptionThrowingHandler(new SocketException((int)SocketError.ConnectionRefused));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        await using var client = new OllamaChatClient(httpClient, TestModel);

        var act = async () => await CollectAsync(client.GenerateStreamingAsync("test", []));

        await act.Should().ThrowAsync<OllamaConnectionException>();
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a pre-cancelled token propagates as <see cref="OperationCanceledException"/> (not wrapped) (AC #8).
    /// </summary>
    [Fact]
    public async Task GenerateStreamingAsync_WithPreCancelledToken_ThrowsOperationCanceledExceptionAsync()
    {
        using var httpClient = BuildStreamingClient(ThreeChunkBody);
        await using var client = new OllamaChatClient(httpClient, TestModel);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await CollectAsync(
            client.GenerateStreamingAsync("test", [], cts.Token));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Verifies that a pre-cancelled token through <see cref="OllamaChatClient.GenerateAsync"/> propagates unchanged (AC #8).
    /// </summary>
    [Fact]
    public async Task GenerateAsync_WithPreCancelledToken_ThrowsOperationCanceledExceptionAsync()
    {
        using var httpClient = BuildStreamingClient(ThreeChunkBody);
        await using var client = new OllamaChatClient(httpClient, TestModel);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => client.GenerateAsync("test", [], cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Disposal guards ───────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that calling DisposeAsync twice does not throw (idempotent dispose).
    /// </summary>
    [Fact]
    public async Task DisposeAsync_CalledTwice_IsIdempotentAsync()
    {
        using var httpClient = BuildStreamingClient(ThreeChunkBody);
        var client = new OllamaChatClient(httpClient, TestModel);
        await client.DisposeAsync();

        var act = async () => await client.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Verifies that enumerating after disposal throws <see cref="ObjectDisposedException"/> (AC #9).
    /// </summary>
    [Fact]
    public async Task GenerateStreamingAsync_AfterDispose_ThrowsObjectDisposedExceptionAsync()
    {
        using var httpClient = BuildStreamingClient(ThreeChunkBody);
        var client = new OllamaChatClient(httpClient, TestModel);
        await client.DisposeAsync();

        var act = async () => await CollectAsync(client.GenerateStreamingAsync("test", []));

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    /// <summary>
    /// Verifies that calling <see cref="OllamaChatClient.GenerateAsync"/> after disposal throws <see cref="ObjectDisposedException"/> (AC #9).
    /// </summary>
    [Fact]
    public async Task GenerateAsync_AfterDispose_ThrowsObjectDisposedExceptionAsync()
    {
        using var httpClient = BuildStreamingClient(ThreeChunkBody);
        var client = new OllamaChatClient(httpClient, TestModel);
        await client.DisposeAsync();

        var act = () => client.GenerateAsync("test", []);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    // ── Null argument guards ───────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <see cref="OllamaChatClient.GenerateAsync"/> throws when <c>prompt</c> is null.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_WithNullPrompt_ThrowsArgumentNullExceptionAsync()
    {
        using var httpClient = BuildStreamingClient(ThreeChunkBody);
        await using var client = new OllamaChatClient(httpClient, TestModel);

        var act = () => client.GenerateAsync(null!, []);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// Verifies that <see cref="OllamaChatClient.GenerateAsync"/> throws when <c>context</c> is null.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_WithNullContext_ThrowsArgumentNullExceptionAsync()
    {
        using var httpClient = BuildStreamingClient(ThreeChunkBody);
        await using var client = new OllamaChatClient(httpClient, TestModel);

        var act = () => client.GenerateAsync("test", null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// Verifies that <see cref="OllamaChatClient.GenerateStreamingAsync"/> throws when <c>prompt</c> is null.
    /// </summary>
    [Fact]
    public async Task GenerateStreamingAsync_WithNullPrompt_ThrowsArgumentNullExceptionAsync()
    {
        using var httpClient = BuildStreamingClient(ThreeChunkBody);
        await using var client = new OllamaChatClient(httpClient, TestModel);

        var act = async () => await CollectAsync(client.GenerateStreamingAsync(null!, []));

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// Verifies that <see cref="OllamaChatClient.GenerateStreamingAsync"/> throws when <c>context</c> is null.
    /// </summary>
    [Fact]
    public async Task GenerateStreamingAsync_WithNullContext_ThrowsArgumentNullExceptionAsync()
    {
        using var httpClient = BuildStreamingClient(ThreeChunkBody);
        await using var client = new OllamaChatClient(httpClient, TestModel);

        var act = async () => await CollectAsync(client.GenerateStreamingAsync("test", null!));

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
