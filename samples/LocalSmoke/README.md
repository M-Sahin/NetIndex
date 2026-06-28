# LocalSmoke — NetIndex local end-to-end sample

A minimal console app that exercises the **real** NetIndex pipeline end-to-end
against a fully local stack:

- **Embeddings + chat**: [Ollama](https://ollama.com) (`nomic-embed-text` + `llama3.2`)
- **Vector store**: SQLite

It references the NetIndex source directly via `ProjectReference` — **no NuGet
packages and no publishing required** — so it's a quick way to confirm a local
setup actually works (ingest → vector search → grounded generation).

## Prerequisites

1. [Install Ollama](https://ollama.com/download) and make sure it's running
   (`ollama serve`).
2. Pull the models:

   ```bash
   ollama pull nomic-embed-text
   ollama pull llama3.2
   ```

## Run

```bash
dotnet run --project samples/LocalSmoke
```

Expected output (scores will vary slightly by model build):

```
[2/3] Querying: "how do I get the espresso machine working again?"
      [0.765] doc-coffee: The office espresso machine is a Rocket Appartamento...
      [0.491] doc-vpn: To connect to the corporate VPN...
      [0.386] doc-parking: Visitor parking is on level B2...

[3/3] Generating grounded answer...
      To reset the office espresso machine, hold the power button for ten
      seconds until both lights blink, then release.
```

## Configuration

Override the defaults via environment variables:

| Variable             | Default                   |
| -------------------- | ------------------------- |
| `OLLAMA_ENDPOINT`    | `http://localhost:11434`  |
| `OLLAMA_EMBED_MODEL` | `nomic-embed-text`        |
| `OLLAMA_CHAT_MODEL`  | `llama3.2`                |

> If you use a different embedding model, update `EmbeddingDimensions` in
> `Program.cs` to match its output size (the SQLite store and embedding
> generator dimensions must agree, or `Build()` fails fast).

## Production note

`LocalDevTenantResolver` allows all operations to satisfy the deny-all default.
**Never use it in production** — configure a real `ITenantResolver` (e.g.
`ClaimsTenantResolver` from `NetIndex.AspNetCore`) that validates real
credentials.
