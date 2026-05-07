# NetIndex

**Production-grade RAG framework for .NET 9.** Ingest documents, embed vectors, search semantically, and stream LLM answers — all in C#, wired into your existing DI container.

[![Build](https://github.com/M-Sahin/rag-pipeline-net/actions/workflows/main.yml/badge.svg)](https://github.com/M-Sahin/rag-pipeline-net/actions/workflows/main.yml)
[![PR Gate](https://github.com/M-Sahin/rag-pipeline-net/actions/workflows/pr.yml/badge.svg)](https://github.com/M-Sahin/rag-pipeline-net/actions/workflows/pr.yml)
[![NuGet](https://img.shields.io/nuget/v/NetIndex.Core?color=004880)](https://www.nuget.org/packages/NetIndex.Core)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![OpenTelemetry](https://img.shields.io/badge/OpenTelemetry-compatible-f5a800)](https://opentelemetry.io)

---

Think LlamaIndex or LangChain, but built from the ground up for .NET — not a port, not a wrapper. NetIndex uses `Microsoft.Extensions.DependencyInjection`, `IAsyncEnumerable<T>` for streaming, and `System.Diagnostics.Activity` for tracing. Zero external dependencies in the core contracts package.

## Quick start

**Local development** (Ollama + SQLite, no cloud accounts needed):

```csharp
services.AddNetIndex(builder => builder
    .UseOllamaEmbedding("http://localhost:11434", "nomic-embed-text")
    .UseSqliteVectorStore("Data Source=vectors.db")
    .UseFixedSizeChunking(opts => opts.ChunkSize = 512)
    .Build());
```

**Production** (Azure OpenAI + pgvector):

```csharp
services.AddNetIndex(builder => builder
    .UseAzureOpenAIEmbedding(opts => opts.Endpoint = "https://corp.openai.azure.com/")
    .UsePgvector(builder.Configuration.GetConnectionString("Postgres"))
    .UseRecursiveChunking()
    .Build());
```

Then inject `INetIndexPipeline` and use it:

```csharp
// Ingest
await pipeline.IngestAsync(document);

// Semantic search
await foreach (var result in pipeline.QueryAsync("how do I reset my password?"))
    Console.WriteLine($"[{result.Score:F3}] {result.Item.Content}");

// Streaming generation
await foreach (var chunk in pipeline.GenerateAsync(query))
    await response.WriteAsync(chunk.Text);
```

## Why NetIndex

Most RAG implementations in .NET are glue code around OpenAI's REST API. NetIndex is a full framework:

- **Build-time validation** — `Build()` spins up a temporary DI container and validates dimension parity between your embedding provider and vector store. Wiring a 1536-dim OpenAI embedder to a 768-dim store throws `NetIndexConfigurationException` at startup, not at query time.
- **Deny-all security default** — The default `ITenantResolver` rejects every request. Nothing leaks accidentally in development. Bring your own resolver to integrate with JWT claims, ASP.NET Core Identity, or Azure Entra ID.
- **Streaming first** — `QueryAsync` and `GenerateAsync` both return `IAsyncEnumerable<T>`. No intermediate buffering; real SSE streaming to browsers works out of the box.
- **Typed exceptions** — Every failure mode has a named exception type with structured `Exception.Data` fields. `NetIndexProviderException` has an `IsTransient` flag so you can wire it straight into Polly.

## Pipeline overview

```
Ingest:   Document → Chunk → Embed → Upsert
Query:    string   → Embed → VectorSearch → (Rerank) → IAsyncEnumerable<SearchResult>
Generate: string   → Query → LLM stream   → IAsyncEnumerable<GenerationChunk>
```

Every operation begins with `ITenantResolver.ResolveTenantIdAsync()`. Authorization is not optional.

## Pluggable architecture

Every stage is an interface. Swap implementations without touching application code:

| Stage | Interface | Included implementations |
|---|---|---|
| Ingestion | `IDocumentLoader<TFormat>` | PDF (iTextSharp), DOCX (OpenXml), Markdown, Tesseract OCR |
| Chunking | `IChunkingStrategy` | FixedSize, Recursive, Semantic |
| Embedding | `IEmbeddingGenerator` | Ollama, OpenAI, Azure OpenAI |
| Vector store | `IVectorStore` | InMemory, SQLite (sqlite-vec), pgvector |
| LLM | `IChatClient` | Ollama, OpenAI, Azure OpenAI |
| Auth | `ITenantResolver` | `DenyAllTenantResolver` (default), bring your own |
| Reranking | `IDocumentReranker` | Bring your own cross-encoder |

### Chunking strategies

- **FixedSize** — Splits on `\n\n`, merges segments up to a character budget (1 token ≈ 4 chars). Configurable overlap. Good for uniform technical documents.
- **Recursive** — FixedSize first pass; oversized chunks get re-chunked with the Semantic strategy. Good for mixed-content documents.
- **Semantic** — Sentence-level splitting, candidate grouping, batch embedding, cosine similarity threshold (default 0.7) to find topic boundaries. Good for narrative content, research papers, legal documents.

## Packages

```
NetIndex.Core.Abstractions    — interfaces and contracts (zero external deps)
NetIndex.Core                 — pipeline orchestration, builder, DI

NetIndex.Providers.Ollama     — local LLM via OllamaSharp
NetIndex.Providers.OpenAI     — OpenAI embeddings + chat
NetIndex.Providers.AzureOpenAI — Azure OpenAI with DefaultAzureCredential

NetIndex.Storage.InMemory     — volatile, for tests and local dev
NetIndex.Storage.Sqlite       — file-backed via sqlite-vec
NetIndex.Storage.Pgvector     — PostgreSQL with IVFFlat / HNSW indexes

NetIndex.Ingestion.Pdf        — PDF text extraction
NetIndex.Ingestion.Docx       — DOCX/OpenXml (body, tables, headers, footers)
NetIndex.Ingestion.Markdown   — Markdown with YAML frontmatter extraction
NetIndex.Ingestion.Tesseract  — OCR for scanned documents

NetIndex.AspNetCore           — middleware, hosted services, HttpContext tenant resolver
```

## Observability

NetIndex emits OpenTelemetry spans via `System.Diagnostics.ActivitySource` — no dependency on the OTel SDK required in the core packages. Wire it up in your host:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddSource("NetIndex")
        .AddOtlpExporter());
```

Span names follow the `netindex.{stage}` convention: `netindex.ingest`, `netindex.query`, `netindex.generate`, `netindex.embed`, `netindex.store.upsert`, `netindex.store.query`.

## Testing

The `VectorStoreContractSuite` base class validates the full `IVectorStore` contract — empty queries, upsert/query round-trips, idempotent upserts, cancellation, dimension mismatch errors, and reset semantics. Inherit it to test any new storage backend automatically.

Architecture dependency rules are enforced at every PR via NetArchTest. Sibling layers (Providers, Storage, Ingestion) must never reference each other.

```bash
dotnet test NetIndex.sln                            # all tests
dotnet test NetIndex.sln --filter "Category=ArchContract|Category=SecurityContract|Category=PipelineContract"  # PR gate only
```

## Roadmap

**V1 — 2026 (current)**

- [x] Core abstractions, pipeline orchestration, builder + DI
- [x] Ollama, OpenAI, Azure OpenAI providers
- [x] InMemory, SQLite, pgvector storage
- [x] PDF, DOCX, Markdown ingestion + OCR
- [x] FixedSize, Recursive, Semantic chunking
- [x] Multi-tenant auth with deny-all default
- [x] OpenTelemetry tracing, typed exception hierarchy
- [x] ASP.NET Core integration, contract test suite
- [ ] Semantic Kernel integration
- [ ] RAG evaluation harness

**V2 — 2026+**

- Multi-stage retrieval (query expansion, HyDE)
- Hybrid search (dense + BM25 sparse)
- Cross-encoder reranking
- Agent orchestration patterns

## Open core

The full `NetIndex.Core` repository is Apache-2.0. Enterprise add-ons (RBAC, audit dashboard, managed hosting, compliance packs, priority support) are available under commercial terms.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). All PRs run the full contract + architecture test gate before merge.

## License

[Apache-2.0](LICENSE) — core packages and all tests.
