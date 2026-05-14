// --- Services ---

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNetIndex(netIndex =>
{
    netIndex.UseAzureOpenAI(builder.Configuration.GetSection("NetIndex:AzureOpenAI"));
    netIndex.UsePgvector(builder.Configuration.GetSection("NetIndex:Pgvector"));

    // 🔁 LOCAL DEV: comment the two lines above and uncomment the two below to
    //              run with Ollama + SQLite — no cloud accounts required.
    // netIndex.UseOllama(builder.Configuration.GetSection("NetIndex:Ollama"));
    // netIndex.UseSqlite(builder.Configuration.GetSection("NetIndex:Sqlite"));
});

// --- Pipeline ---

var app = builder.Build();

// --- Endpoints ---

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.Run();
