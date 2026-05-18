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
using Xunit;

namespace NetIndex.Template.Tests;

/// <summary>
/// CI-safe in-process integration test that boots a mirror host matching the template's
/// post-swap (Ollama + SQLite active) wiring, with no-network fakes substituted for
/// real Ollama and SQLite providers. Verifies that the /ingest and /query endpoint
/// handlers compose correctly with the NetIndex pipeline (Story 4.3 AC#7).
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
            endpoints.MapPost("/ingest", async context =>
            {
                var pipeline = context.RequestServices.GetRequiredService<INetIndexPipeline>();
                var body = await context.Request.ReadFromJsonAsync<IngestPayload>();
                if (body is null || string.IsNullOrWhiteSpace(body.Id) || string.IsNullOrWhiteSpace(body.Content))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return;
                }

                var doc = new SimpleDocument(body.Id, body.Content);
                await pipeline.IngestAsync(doc, context.RequestAborted);

                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { id = body.Id });
            });

            endpoints.MapGet("/query", async context =>
            {
                var q = context.Request.Query["q"].ToString();
                if (string.IsNullOrWhiteSpace(q))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return;
                }

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

                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(results);
            });
        });
    }

    // ── Tests ──

    [Fact]
    public async Task PostIngest_WithValidDocument_Returns200OkAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/ingest",
            new { Id = "doc-integration-1", Content = "NetIndex is a .NET RAG framework." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        body.Should().NotBeNull();
        body!.RootElement.GetProperty("id").GetString().Should().Be("doc-integration-1");
    }

    [Fact]
    public async Task GetQuery_AfterIngest_Returns200OkWithMatchingChunkAsync()
    {
        // Arrange: ingest a document first
        await _client.PostAsJsonAsync(
            "/ingest",
            new { Id = "doc-integration-2", Content = "Retrieval-Augmented Generation with .NET and NetIndex." });

        // Act
        var response = await _client.GetAsync("/query?q=retrieval+augmented");

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
    public async Task GetQuery_WithBlankQ_Returns400BadRequestAsync()
    {
        var response = await _client.GetAsync("/query?q=");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Helpers ──

    private sealed record IngestPayload(string Id, string Content);

    private sealed record SimpleDocument(
        string Id,
        string Content) : IDocument
    {
        public IReadOnlyDictionary<string, string>? Metadata => null;
        public Uri? SourceUri => null;
    }
}
