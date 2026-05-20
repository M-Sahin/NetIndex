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

app.MapPost("/ingest", async (IngestRequest body, INetIndexPipeline pipeline, CancellationToken ct) =>
{
    var document = new TemplateDocument(body.Id, body.Content);
    await pipeline.IngestAsync(document, ct);
    return Results.Ok(new { id = body.Id });
});

app.MapGet("/query", async (string q, INetIndexPipeline pipeline, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(q))
    {
        return Results.BadRequest(new { error = "Query parameter 'q' is required." });
    }

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

    return Results.Ok(results);
});

app.Run();

// Minimal request body for /ingest
internal sealed record IngestRequest(string Id, string Content);
