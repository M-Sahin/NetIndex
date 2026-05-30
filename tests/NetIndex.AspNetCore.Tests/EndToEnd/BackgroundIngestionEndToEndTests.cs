using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetIndex.AspNetCore.BackgroundServices;
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using Xunit;

namespace NetIndex.AspNetCore.Tests.EndToEnd;

/// <summary>
/// End-to-end test: a POST enqueues a document, returns immediately, and the background hosted
/// service ingests it (replaying the captured tenant) so a later query finds it.
/// </summary>
public class BackgroundIngestionEndToEndTests
{
    /// <summary>
    /// POST /api/ingest returns 202 immediately; the document is ingested in the background under
    /// the captured tenant and becomes queryable.
    /// </summary>
    [Fact]
    public async Task EndToEnd_PostIngest_EnqueuesAndBackgroundServiceIngestsAsync()
    {
        await using var factory = new BackgroundIngestionWebApplicationFactory();
        var client = factory.CreateClient();

        var ingest = new HttpRequestMessage(HttpMethod.Post, "/api/ingest?id=doc-1&content=hello%20world");
        ingest.Headers.Add("X-Tenant-Id", "acme");
        var ingestResponse = await client.SendAsync(ingest);

        ingestResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await PollAsync(async () =>
        {
            var query = new HttpRequestMessage(HttpMethod.Get, "/api/query?q=hello");
            query.Headers.Add("X-Tenant-Id", "acme");
            var response = await client.SendAsync(query);
            return await response.Content.ReadAsStringAsync();
        });

        body.Should().Contain("doc-1");
    }

    /// <summary>Polls an async producer until it returns a value containing "doc-1" or times out (~5s).</summary>
    private static async Task<string> PollAsync(Func<Task<string>> produce)
    {
        var stopwatch = Stopwatch.StartNew();
        var last = string.Empty;
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            last = await produce();
            if (last.Contains("doc-1", StringComparison.Ordinal))
            {
                return last;
            }

            await Task.Delay(50);
        }

        return last;
    }
}

internal sealed class BackgroundIngestionWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseContentRoot(AppContext.BaseDirectory);
        return base.CreateHost(builder);
    }

    protected override IHostBuilder CreateHostBuilder()
    {
        return new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetIndex(net =>
                    {
                        net.UseAspNetCoreTenant();
                        net.UseBackgroundIngestion();
                    }).Build();
                });
                web.Configure(app =>
                {
                    app.UseNetIndexTenant();
                    app.UseRouting();
#pragma warning disable ASP0014
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapPost("/api/ingest", async context =>
                        {
                            var queue = context.RequestServices.GetRequiredService<IIngestionQueue>();
                            var id = context.Request.Query["id"].ToString();
                            var content = context.Request.Query["content"].ToString();
                            await queue.EnqueueAsync(new EndToEndTestDocument(id, content), context.RequestAborted);
                            context.Response.StatusCode = 202;
                        });

                        endpoints.MapGet("/api/query", async context =>
                        {
                            var pipeline = context.RequestServices.GetRequiredService<INetIndexPipeline>();
                            var q = context.Request.Query["q"].ToString();
                            var ids = new List<string>();
                            try
                            {
                                await foreach (var result in pipeline.QueryAsync(q, context.RequestAborted))
                                {
                                    ids.Add(result.Item.DocumentId);
                                }
                            }
                            catch (NetIndexAuthorizationException)
                            {
                                context.Response.StatusCode = 401;
                                return;
                            }

                            await context.Response.WriteAsync(string.Join(",", ids));
                        });
                    });
#pragma warning restore ASP0014
                });
            });
    }
}

internal sealed class EndToEndTestDocument : IDocument
{
    public EndToEndTestDocument(string id, string content)
    {
        Id = id;
        Content = content;
    }

    public string Id { get; }

    public string Content { get; }

    public IReadOnlyDictionary<string, string>? Metadata => null;

    public Uri? SourceUri => null;
}
