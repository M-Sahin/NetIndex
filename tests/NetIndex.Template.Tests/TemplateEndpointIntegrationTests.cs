#pragma warning disable CS1591
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace NetIndex.Template.Tests;

/// <summary>
/// CI-safe in-process integration tests that boot a mirror host matching the template's
/// post-swap (Ollama + SQLite active) wiring, with no-network fakes substituted for
/// real Ollama and SQLite providers. Verifies that the /api/* endpoint handlers compose
/// correctly with the NetIndex pipeline (Story 4.4 AC#6, AC#7).
/// </summary>
[Trait("Category", "Integration")]
public sealed class TemplateEndpointIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(ConfigureTestServices);
                web.Configure(ConfigureTestApp);
            })
            .StartAsync();

        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    // ── Mirrors Program.cs service wiring with fakes instead of real providers ──

    private static void ConfigureTestServices(IServiceCollection services)
    {
        // Routing is required by UseRouting() / UseEndpoints() below
        services.AddRouting();

        // Allow-all resolver — replaces DenyAllTenantResolver default
        var resolver = Substitute.For<ITenantResolver>();
        resolver.ResolveTenantIdAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("local-dev"));
        resolver.ResolveClaimsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string> { ["tenant_id"] = "local-dev" }));
        services.AddSingleton<ITenantResolver>(resolver);

        // AddNetIndex with no real providers — uses InMemoryEmbeddingGenerator (384-dim)
        // + InMemoryVectorStore (384-dim) + InMemoryChatClient as defaults.
        // Dimensions match → Build() succeeds without Ollama.
        services.AddNetIndex(_ => { }).Build();
    }

    private static void ConfigureTestApp(IApplicationBuilder app)
    {
        app.UseRouting();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapPost("/api/ingest", async context =>
            {
                var pipeline = context.RequestServices.GetRequiredService<INetIndexPipeline>();
                var body = await context.Request.ReadFromJsonAsync<IngestPayload>();
                if (body is null || string.IsNullOrWhiteSpace(body.Id) || string.IsNullOrWhiteSpace(body.Content))
                {
                    await Results.Problem(
                        statusCode: 400,
                        type: "https://netindex.dev/errors/validation",
                        title: "Validation failed",
                        detail: "Both 'id' and 'content' must be non-blank."
                    ).ExecuteAsync(context);
                    return;
                }

                try
                {
                    var doc = new SimpleDocument(body.Id, body.Content);
                    await pipeline.IngestAsync(doc, context.RequestAborted);
                    await Results.Ok(new { id = body.Id }).ExecuteAsync(context);
                }
                catch (NetIndexAuthorizationException ex)
                {
                    await Results.Problem(
                        statusCode: 401,
                        type: "https://netindex.dev/errors/authorization",
                        title: "Authorization denied",
                        detail: ex.Message
                    ).ExecuteAsync(context);
                }
                catch (NetIndexProviderException ex)
                {
                    await Results.Problem(
                        statusCode: 502,
                        type: "https://netindex.dev/errors/provider",
                        title: "Provider failure",
                        detail: ex.Message,
                        extensions: new Dictionary<string, object?> { ["retryable"] = ex.IsRetryable }
                    ).ExecuteAsync(context);
                }
                catch (NetIndexException ex)
                {
                    await Results.Problem(
                        statusCode: 500,
                        type: "https://netindex.dev/errors/internal",
                        title: "NetIndex error",
                        detail: ex.Message
                    ).ExecuteAsync(context);
                }
            });

            endpoints.MapGet("/api/query", async context =>
            {
                var q = context.Request.Query["q"].ToString();
                if (string.IsNullOrWhiteSpace(q))
                {
                    await Results.Problem(
                        statusCode: 400,
                        type: "https://netindex.dev/errors/validation",
                        title: "Validation failed",
                        detail: "Query parameter 'q' must be non-blank."
                    ).ExecuteAsync(context);
                    return;
                }

                if (!int.TryParse(context.Request.Query["top"].ToString(), out var topValue))
                {
                    topValue = 5;
                }

                if (topValue < 1 || topValue > 50)
                {
                    await Results.Problem(
                        statusCode: 400,
                        type: "https://netindex.dev/errors/validation",
                        title: "Validation failed",
                        detail: "'top' must be between 1 and 50."
                    ).ExecuteAsync(context);
                    return;
                }

                try
                {
                    var pipeline = context.RequestServices.GetRequiredService<INetIndexPipeline>();
                    var results = new List<object>();
                    await foreach (var result in pipeline.QueryAsync(q, context.RequestAborted))
                    {
                        results.Add(new
                        {
                            documentId = result.Item.DocumentId,
                            score = result.Score,
                            text = result.Item.Text,
                        });
                    }

                    await Results.Ok(results.Take(topValue).ToList()).ExecuteAsync(context);
                }
                catch (NetIndexAuthorizationException ex)
                {
                    await Results.Problem(
                        statusCode: 401,
                        type: "https://netindex.dev/errors/authorization",
                        title: "Authorization denied",
                        detail: ex.Message
                    ).ExecuteAsync(context);
                }
                catch (NetIndexProviderException ex)
                {
                    await Results.Problem(
                        statusCode: 502,
                        type: "https://netindex.dev/errors/provider",
                        title: "Provider failure",
                        detail: ex.Message,
                        extensions: new Dictionary<string, object?> { ["retryable"] = ex.IsRetryable }
                    ).ExecuteAsync(context);
                }
                catch (NetIndexException ex)
                {
                    await Results.Problem(
                        statusCode: 500,
                        type: "https://netindex.dev/errors/internal",
                        title: "NetIndex error",
                        detail: ex.Message
                    ).ExecuteAsync(context);
                }
            });

            endpoints.MapPost("/api/generate", async context =>
            {
                var body = await context.Request.ReadFromJsonAsync<GeneratePayload>();
                if (body is null || string.IsNullOrWhiteSpace(body.Query))
                {
                    await Results.Problem(
                        statusCode: 400,
                        type: "https://netindex.dev/errors/validation",
                        title: "Validation failed",
                        detail: "Request body 'query' must be non-blank."
                    ).ExecuteAsync(context);
                    return;
                }

                context.Response.Headers["Cache-Control"] = "no-cache";
                context.Response.Headers["X-Accel-Buffering"] = "no";
                context.Response.ContentType = "text/event-stream";

                try
                {
                    var pipeline = context.RequestServices.GetRequiredService<INetIndexPipeline>();
                    await foreach (var chunk in pipeline.GenerateAsync(body.Query, context.RequestAborted))
                    {
                        var json = JsonSerializer.Serialize(new
                        {
                            text = chunk.Text,
                            isComplete = chunk.IsComplete,
                            finishReason = chunk.FinishReason.ToString()
                        });
                        await context.Response.WriteAsync($"data: {json}\n\n", context.RequestAborted);
                        await context.Response.Body.FlushAsync(context.RequestAborted);
                    }
                }
                catch (NetIndexAuthorizationException ex) when (!context.Response.HasStarted)
                {
                    await Results.Problem(
                        statusCode: 401,
                        type: "https://netindex.dev/errors/authorization",
                        title: "Authorization denied",
                        detail: ex.Message
                    ).ExecuteAsync(context);
                }
                catch (NetIndexProviderException ex) when (!context.Response.HasStarted)
                {
                    await Results.Problem(
                        statusCode: 502,
                        type: "https://netindex.dev/errors/provider",
                        title: "Provider failure",
                        detail: ex.Message,
                        extensions: new Dictionary<string, object?> { ["retryable"] = ex.IsRetryable }
                    ).ExecuteAsync(context);
                }
                catch (NetIndexException ex) when (!context.Response.HasStarted)
                {
                    await Results.Problem(
                        statusCode: 500,
                        type: "https://netindex.dev/errors/internal",
                        title: "NetIndex error",
                        detail: ex.Message
                    ).ExecuteAsync(context);
                }
            });
        });
    }

    // ── Tests ──

    [Fact]
    public async Task PostApiIngest_WithValidDocument_Returns200OkAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/ingest",
            new { Id = "doc-integration-1", Content = "NetIndex is a .NET RAG framework." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        body.Should().NotBeNull();
        body!.RootElement.GetProperty("id").GetString().Should().Be("doc-integration-1");
    }

    [Fact]
    public async Task GetApiQuery_AfterIngest_Returns200OkWithMatchingChunkAsync()
    {
        // Arrange: ingest a document first
        await _client.PostAsJsonAsync(
            "/api/ingest",
            new { Id = "doc-integration-2", Content = "Retrieval-Augmented Generation with .NET and NetIndex." });

        // Act
        var response = await _client.GetAsync("/api/query?q=retrieval+augmented");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var results = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        results.Should().NotBeNull().And.NotBeEmpty();

        var first = results![0];
        first.GetProperty("documentId").GetString().Should().Be("doc-integration-2",
            "the ingested document's ID must appear in query results");
        first.GetProperty("score").GetSingle().Should().BeGreaterThan(0,
            "similarity score must be positive for a matching document");
        first.GetProperty("text").GetString().Should().NotBeNullOrWhiteSpace(
            "the matched chunk text must be returned");
    }

    [Fact]
    public async Task GetApiQuery_WithBlankQ_Returns400BadRequestProblemDetailsAsync()
    {
        var response = await _client.GetAsync("/api/query?q=");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        body.Should().NotBeNull();
        body!.RootElement.GetProperty("type").GetString().Should().NotBeNullOrWhiteSpace();
        body.RootElement.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        body.RootElement.GetProperty("status").GetInt32().Should().Be(400);
    }

    [Fact]
    public async Task GetApiQuery_WithTopOutOfRange_Returns400ProblemDetailsAsync()
    {
        // top=0 is below the minimum of 1
        var responseZero = await _client.GetAsync("/api/query?q=test&top=0");
        responseZero.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "top=0 is below the allowed minimum of 1");
        responseZero.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        // top=51 is above the maximum of 50
        var responseOver = await _client.GetAsync("/api/query?q=test&top=51");
        responseOver.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "top=51 is above the allowed maximum of 50");
        responseOver.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task PostApiGenerate_WithValidQuery_StreamsTextEventStreamAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/generate",
            new { Query = "What is NetIndex?" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        var dataLines = new List<string>();
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                dataLines.Add(line);
            }
        }

        dataLines.Should().NotBeEmpty("at least one SSE data frame must be received");

        // The final frame must have isComplete: true
        var lastJson = dataLines[^1]["data: ".Length..];
        var lastChunk = JsonDocument.Parse(lastJson);
        lastChunk.RootElement.GetProperty("isComplete").GetBoolean().Should().BeTrue(
            "the final SSE frame must have isComplete: true");
    }

    [Fact]
    public async Task PostApiGenerate_AuthorizationFailure_Returns401ProblemDetailsAsync()
    {
        // Build a dedicated mini-host whose resolver always throws.
        var failingResolver = Substitute.For<ITenantResolver>();
        failingResolver.ResolveTenantIdAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new NetIndexAuthorizationException("Access denied."));
        failingResolver.ResolveClaimsAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new NetIndexAuthorizationException("Access denied."));

        var authHost = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<ITenantResolver>(failingResolver);
                    services.AddNetIndex(_ => { }).Build();
                });
                web.Configure(ConfigureTestApp);
            })
            .StartAsync();

        try
        {
            using var authClient = authHost.GetTestClient();

            var response = await authClient.PostAsJsonAsync(
                "/api/generate",
                new { Query = "test query" });

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

            var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
            body.Should().NotBeNull();
            body!.RootElement.GetProperty("type").GetString().Should().Contain("authorization",
                "problem type must identify the authorization error category");
            body.RootElement.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace(
                "problem title must be present");
            body.RootElement.GetProperty("status").GetInt32().Should().Be(401,
                "problem status must match the HTTP status code");
        }
        finally
        {
            await authHost.StopAsync();
            authHost.Dispose();
        }
    }

    // ── Helpers ──

    private sealed record IngestPayload(string Id, string Content);

    private sealed record GeneratePayload(string Query);

    private sealed record SimpleDocument(
        string Id,
        string Content) : IDocument
    {
        public IReadOnlyDictionary<string, string>? Metadata => null;
        public Uri? SourceUri => null;
    }
}
