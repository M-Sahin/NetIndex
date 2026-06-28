using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.Ollama;
using NetIndex.Samples.LocalSmoke;
using NetIndex.Storage.Sqlite;

// ---------------------------------------------------------------------------
// NetIndex local end-to-end sample.
//
// Wires the REAL pipeline against a local Ollama (embeddings + chat) and a
// SQLite vector store, using project references only — no NuGet packages, no
// publishing. Proves ingest -> query -> generate actually works locally.
//
// Prerequisites:
//   ollama pull nomic-embed-text
//   ollama pull llama3.2
//
// Run:
//   dotnet run --project samples/LocalSmoke
//
// Override models via env vars if you pulled different ones:
//   OLLAMA_EMBED_MODEL  (default: nomic-embed-text, 768 dims)
//   OLLAMA_CHAT_MODEL   (default: llama3.2)
//   OLLAMA_ENDPOINT     (default: http://localhost:11434)
// ---------------------------------------------------------------------------

const int EmbeddingDimensions = 768; // nomic-embed-text output size

var endpoint = Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT") ?? "http://localhost:11434";
var embedModel = Environment.GetEnvironmentVariable("OLLAMA_EMBED_MODEL") ?? "nomic-embed-text";
var chatModel = Environment.GetEnvironmentVariable("OLLAMA_CHAT_MODEL") ?? "llama3.2";

// Fresh DB each run so the sample is repeatable.
var dbPath = Path.Combine(AppContext.BaseDirectory, "smoke.db");
if (File.Exists(dbPath))
{
    File.Delete(dbPath);
}

Console.WriteLine("=== NetIndex local E2E sample ===");
Console.WriteLine($"  Ollama endpoint : {endpoint}");
Console.WriteLine($"  embedding model : {embedModel} ({EmbeddingDimensions} dims)");
Console.WriteLine($"  chat model      : {chatModel}");
Console.WriteLine($"  vector store    : SQLite ({dbPath})");
Console.WriteLine();

var services = new ServiceCollection();

// Override the deny-all default so operations are authorized in this local demo.
services.AddSingleton<ITenantResolver, LocalDevTenantResolver>();

services.AddNetIndex(netIndex => netIndex
    .UseOllama(o =>
    {
        o.Endpoint = endpoint;
        o.Model = embedModel;
        o.Dimensions = EmbeddingDimensions;
    })
    .UseOllamaChatClient(o =>
    {
        o.Endpoint = endpoint;
        o.Model = chatModel;
    })
    .UseSqlite($"Data Source={dbPath}", o => o.Dimensions = EmbeddingDimensions)
    .Build());

await using var provider = services.BuildServiceProvider();
var pipeline = provider.GetRequiredService<INetIndexPipeline>();

// A tiny knowledge base to ingest.
var documents = new IDocument[]
{
    new SampleDocument("doc-coffee",
        "The office espresso machine is a Rocket Appartamento. To reset it, hold the " +
        "power button for ten seconds until both lights blink, then release."),
    new SampleDocument("doc-vpn",
        "To connect to the corporate VPN, open the GlobalConnect client and sign in with " +
        "your employee ID. If it times out, switch from UDP to TCP in Settings > Protocol."),
    new SampleDocument("doc-parking",
        "Visitor parking is on level B2. Employees park on B3 and B4. Badge access is " +
        "required after 8pm; contact facilities to enable after-hours access."),
};

try
{
    // --- 1. Ingest ---------------------------------------------------------
    Console.WriteLine("[1/3] Ingesting documents...");
    foreach (var doc in documents)
    {
        await pipeline.IngestAsync(doc);
        Console.WriteLine($"      + {doc.Id}");
    }
    Console.WriteLine();

    // --- 2. Query (vector similarity search) -------------------------------
    const string question = "how do I get the espresso machine working again?";
    Console.WriteLine($"[2/3] Querying: \"{question}\"");
    await foreach (var result in pipeline.QueryAsync(question))
    {
        var score = result.Score.ToString("F3", CultureInfo.InvariantCulture);
        Console.WriteLine($"      [{score}] {result.DocumentId}: {Truncate(result.Item.Text, 80)}");
    }
    Console.WriteLine();

    // --- 3. Generate (RAG: retrieve + LLM) ---------------------------------
    Console.WriteLine($"[3/3] Generating grounded answer...");
    Console.Write("      ");
    await foreach (var chunk in pipeline.GenerateAsync(question))
    {
        Console.Write(chunk.Text);
    }
    Console.WriteLine();
    Console.WriteLine();
    Console.WriteLine("=== OK: ingest -> query -> generate completed ===");
}
catch (NetIndexProviderException ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"PROVIDER ERROR (retryable={ex.IsRetryable}): {ex.Message}");
    Console.Error.WriteLine("Is Ollama running and are the models pulled?");
    Console.Error.WriteLine($"  ollama serve");
    Console.Error.WriteLine($"  ollama pull {embedModel}");
    Console.Error.WriteLine($"  ollama pull {chatModel}");
    Environment.ExitCode = 1;
}

static string Truncate(string s, int max) =>
    s.Length <= max ? s : string.Concat(s.AsSpan(0, max), "...");
