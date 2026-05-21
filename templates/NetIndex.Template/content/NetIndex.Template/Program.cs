using System.Text.Json;
using NetIndex.Core.Abstractions;
using NetIndex.Template;

// --- Services ---

var builder = WebApplication.CreateBuilder(args);

// ⚠ DEV ONLY: LocalDevTenantResolver allows all operations without real auth.
//             Replace with a real ITenantResolver before serving production traffic.
builder.Services.AddSingleton<ITenantResolver, LocalDevTenantResolver>();

builder.Services.AddNetIndex(netIndex =>
{
    netIndex.UseAzureOpenAI(builder.Configuration.GetSection("NetIndex:AzureOpenAI"));
    netIndex.UsePgvector(builder.Configuration.GetSection("NetIndex:Pgvector"));

    // 🔁 LOCAL DEV: comment the two lines above and uncomment the two below to
    //              run with Ollama + SQLite — no cloud accounts required.
    // netIndex.UseOllama(builder.Configuration.GetSection("NetIndex:Ollama"));
    // netIndex.UseSqlite(builder.Configuration.GetSection("NetIndex:Sqlite"));
}).Build();

// --- Pipeline ---

var app = builder.Build();

// --- Endpoints ---

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.MapPost("/api/ingest", async (IngestRequest body, INetIndexPipeline pipeline, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(body.Id) || string.IsNullOrWhiteSpace(body.Content))
    {
        return Results.Problem(
            statusCode: 400,
            type: "https://netindex.dev/errors/validation",
            title: "Validation failed",
            detail: "Both 'id' and 'content' must be non-blank.");
    }

    try
    {
        var document = new TemplateDocument(body.Id, body.Content);
        await pipeline.IngestAsync(document, ct);
        return Results.Ok(new { id = body.Id });
    }
    catch (NetIndexAuthorizationException ex)
    {
        return Results.Problem(
            statusCode: 401,
            type: "https://netindex.dev/errors/authorization",
            title: "Authorization denied",
            detail: ex.Message);
    }
    catch (NetIndexProviderException ex)
    {
        return Results.Problem(
            statusCode: 502,
            type: "https://netindex.dev/errors/provider",
            title: "Provider failure",
            detail: ex.Message,
            extensions: new Dictionary<string, object?> { ["retryable"] = ex.IsRetryable });
    }
    catch (NetIndexException ex)
    {
        return Results.Problem(
            statusCode: 500,
            type: "https://netindex.dev/errors/internal",
            title: "NetIndex error",
            detail: ex.Message);
    }
});

app.MapGet("/api/query", async (string q, int? top, INetIndexPipeline pipeline, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(q))
    {
        return Results.Problem(
            statusCode: 400,
            type: "https://netindex.dev/errors/validation",
            title: "Validation failed",
            detail: "Query parameter 'q' must be non-blank.");
    }

    var topValue = top ?? 5;
    if (topValue < 1 || topValue > 50)
    {
        return Results.Problem(
            statusCode: 400,
            type: "https://netindex.dev/errors/validation",
            title: "Validation failed",
            detail: "'top' must be between 1 and 50.");
    }

    try
    {
        var results = new List<object>();
        await foreach (var result in pipeline.QueryAsync(q, ct))
        {
            results.Add(new
            {
                documentId = result.Item.DocumentId,
                score = result.Score,
                text = result.Item.Text,
            });
        }

        return Results.Ok(results.Take(topValue).ToList());
    }
    catch (NetIndexAuthorizationException ex)
    {
        return Results.Problem(
            statusCode: 401,
            type: "https://netindex.dev/errors/authorization",
            title: "Authorization denied",
            detail: ex.Message);
    }
    catch (NetIndexProviderException ex)
    {
        return Results.Problem(
            statusCode: 502,
            type: "https://netindex.dev/errors/provider",
            title: "Provider failure",
            detail: ex.Message,
            extensions: new Dictionary<string, object?> { ["retryable"] = ex.IsRetryable });
    }
    catch (NetIndexException ex)
    {
        return Results.Problem(
            statusCode: 500,
            type: "https://netindex.dev/errors/internal",
            title: "NetIndex error",
            detail: ex.Message);
    }
});

app.MapPost("/api/generate", async (HttpContext ctx, INetIndexPipeline pipeline, CancellationToken ct) =>
{
    GenerateRequest? body;
    try
    {
        body = await ctx.Request.ReadFromJsonAsync<GenerateRequest>(ct);
    }
    catch (JsonException)
    {
        await Results.Problem(
            statusCode: 400,
            type: "https://netindex.dev/errors/validation",
            title: "Validation failed",
            detail: "Request body must be valid JSON with a 'query' field."
        ).ExecuteAsync(ctx);
        return;
    }

    if (body is null || string.IsNullOrWhiteSpace(body.Query))
    {
        await Results.Problem(
            statusCode: 400,
            type: "https://netindex.dev/errors/validation",
            title: "Validation failed",
            detail: "Request body 'query' must be non-blank."
        ).ExecuteAsync(ctx);
        return;
    }

    ctx.Response.Headers["Cache-Control"] = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";
    ctx.Response.ContentType = "text/event-stream";

    try
    {
        await foreach (var chunk in pipeline.GenerateAsync(body.Query, ct))
        {
            var json = JsonSerializer.Serialize(new
            {
                text = chunk.Text,
                isComplete = chunk.IsComplete,
                finishReason = chunk.FinishReason.ToString()
            });
            await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }
    catch (NetIndexAuthorizationException ex) when (!ctx.Response.HasStarted)
    {
        await Results.Problem(
            statusCode: 401,
            type: "https://netindex.dev/errors/authorization",
            title: "Authorization denied",
            detail: ex.Message
        ).ExecuteAsync(ctx);
    }
    catch (NetIndexProviderException ex) when (!ctx.Response.HasStarted)
    {
        await Results.Problem(
            statusCode: 502,
            type: "https://netindex.dev/errors/provider",
            title: "Provider failure",
            detail: ex.Message,
            extensions: new Dictionary<string, object?> { ["retryable"] = ex.IsRetryable }
        ).ExecuteAsync(ctx);
    }
    catch (NetIndexException ex) when (!ctx.Response.HasStarted)
    {
        await Results.Problem(
            statusCode: 500,
            type: "https://netindex.dev/errors/internal",
            title: "NetIndex error",
            detail: ex.Message
        ).ExecuteAsync(ctx);
    }
});

app.Run();

// Minimal request body for /api/ingest
internal sealed record IngestRequest(string Id, string Content);

// Minimal request body for /api/generate
internal sealed record GenerateRequest(string Query);
