# NetIndex.Template

An ASP.NET Core 9 Minimal API scaffolded from the `netindex` template. Wired for Azure OpenAI + pgvector by default, with an Ollama + SQLite swap block one comment-toggle away.

## What was scaffolded

- `Program.cs` — Minimal API entry point with `AddNetIndex(...)` wiring, active Azure OpenAI + pgvector configuration, and a `/health` endpoint.
- `appsettings.json` / `appsettings.Development.json` — All four NetIndex provider sub-sections (`AzureOpenAI`, `Pgvector`, `Ollama`, `Sqlite`) with placeholder values. No real secrets are present.
- `{name}.csproj` — SDK-style `Microsoft.NET.Sdk.Web` project referencing all six NetIndex packages.

> **Important:** `AddNetIndex()` registers `DenyAllTenantResolver` by default. This means every pipeline operation is denied until you configure an `ITenantResolver`. Production deployments must configure `ITenantResolver` before serving traffic.

## Configure Azure OpenAI + pgvector (default)

Fill in the placeholder values in `appsettings.json` under the `NetIndex:AzureOpenAI` and `NetIndex:Pgvector` sections:

```json
"AzureOpenAI": {
  "Endpoint": "https://<resource-name>.openai.azure.com/",
  "EmbeddingDeployment": "text-embedding-ada-002",
  "ChatDeployment": "gpt-4o",
  "ApiKey": ""
},
"Pgvector": {
  "ConnectionString": "Host=localhost;Database=rag;Username=postgres;Password=..."
}
```

Leave `ApiKey` empty to use Managed Identity (recommended for production).

> Note: `NetIndex.Providers.AzureOpenAI` and `NetIndex.Storage.Pgvector` are part of Epic 5 and may not yet be published to NuGet.org. If `dotnet restore` fails for these packages, check the [releases page](https://github.com/M-Sahin/NetIndex/releases) for the current published version.

## Switch to Local Development (Ollama + SQLite)

1. Open `Program.cs`.
2. Comment out the two active lines inside `AddNetIndex(...)`:
   ```csharp
   // netIndex.UseAzureOpenAI(builder.Configuration.GetSection("NetIndex:AzureOpenAI"));
   // netIndex.UsePgvector(builder.Configuration.GetSection("NetIndex:Pgvector"));
   ```
3. Uncomment the two swap-block lines immediately below:
   ```csharp
   netIndex.UseOllama(builder.Configuration.GetSection("NetIndex:Ollama"));
   netIndex.UseSqlite(builder.Configuration.GetSection("NetIndex:Sqlite"));
   ```
4. Ensure Ollama is running locally: `ollama serve` (and `ollama pull nomic-embed-text` / `ollama pull mistral` if not already pulled).
5. Run the app: `dotnet run`.

The `appsettings.json` already contains the Ollama and SQLite sections with sensible local defaults — no further edits needed for a first run.

## Run it

```bash
dotnet restore
dotnet run
```

Then verify: `curl http://localhost:5000/health`

> **Note:** Kestrel's default port may vary. Check the console output for the actual URL, or set `ASPNETCORE_URLS=http://localhost:5000` before running to pin it.

Expected response: `{"status":"Healthy"}`

## Next steps

- Configure `ITenantResolver` before serving production traffic. See the [project README](https://github.com/M-Sahin/NetIndex/blob/main/README.md) for architecture guidance.
- Add `/ingest`, `/query`, and `/generate` endpoints (covered in Story 4.4).
- Review [CONTRIBUTING.md](https://github.com/M-Sahin/NetIndex/blob/main/CONTRIBUTING.md) for contribution guidelines.
