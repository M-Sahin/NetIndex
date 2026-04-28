# NetIndex Canonical Noun Registry

This registry maps canonical nouns to their corresponding interfaces, types, and implementations in NetIndex. It is the source of truth for naming and prevents semantic drift.

**This registry is FROZEN before any implementation code begins.**

| # | Noun | Interface/Type | Package | Purpose | Naming Convention |
|---|---|---|---|---|---|
| 1 | Builder | `INetIndexBuilder` | Core.Abstractions | Fluent pipeline configuration | `Use{Feature}(...)` extension |
| 2 | Document | `IDocument<TMetadata>` | Core.Abstractions | Ingested source document | Generic metadata container |
| 3 | Chunk | `RagChunk` | Core.Abstractions | Text segment from chunking | Immutable record |
| 4 | Embedding | `float[]` | Core.Abstractions | Vector representation (1536, 1024, etc.) | Standard .NET array |
| 5 | Result | `SearchResult<T>` | Core.Abstractions | Search result with relevance score | Immutable generic record |
| 6 | Retrieval | `RetrievalResult` | Core.Abstractions | Chunks returned from vector search | Immutable record |
| 7 | Generation | `GenerationChunk` | Core.Abstractions | Token from LLM streaming | Immutable record with completion flag |
| 8 | Query | `string` | Core.Abstractions | User question for semantic search | Plain string or RagQuery DTO |
| 9 | Provider | `IEmbeddingGenerator`, `IChatClient` | Providers.* | External LLM/embedding service | Package per provider |
| 10 | Store | `IVectorStore` | Storage.* | Vector persistence backend | Package per implementation |
| 11 | Loader | `IDocumentLoader<TFormat>` | Ingestion.* | Document format parser | Package per format |
| 12 | Resolver | `ITenantResolver` | Core.Abstractions | Authorization context (tenant ID, claims) | Pluggable, deny-all default |
| 13 | Orchestrator | `NetIndexPipeline` | Core | Coordinator of all pipeline stages | Facade over builders/providers |
| 14 | Context | `NetIndexContext` | Core | Runtime pipeline state | Contains provider instances |
| 15 | Activity | `ActivitySource("NetIndex")` | Core.Abstractions | OpenTelemetry tracing root | System.Diagnostics.Activity |
| 16 | Strategy | `IChunkingStrategy` | Core.Abstractions | Chunking algorithm (fixed, semantic, recursive) | Interface for extensibility |
| 17 | Reranker | `IDocumentReranker` | Core.Abstractions | Re-rank retrieved chunks by relevance | Future expansion point |
| 18 | Metadata | `TMetadata` | Generic | Document-attached custom data | Preserves user context |
| 19 | Scope | Folder structure | src/{Category}/ | Logical grouping (Core, Providers, Storage) | `src/Core/`, `src/Providers/Ollama/` |
| 20 | Exception | `NetIndex{Concern}Exception` | Core.Abstractions | Typed error with context | Hierarchy: Config, Auth, Provider, Storage, OCR |

## Naming Rules

### Interface Naming

- Always: `I{Noun}` (e.g., `IVectorStore`, `IEmbeddingGenerator`)
- No abbreviations: `IDocumentLoader` not `IDocLdr`
- Plural nouns only when semantically correct: `IVectorStores` (collection), `IVectorStore` (single implementation)

### Implementation Naming

- **Production**: `{Noun}{Specificity}` (e.g., `SqliteVectorStore`, `OllamaEmbeddingGenerator`)
- **NullObject**: `DenyAll{Noun}` (e.g., `DenyAllTenantResolver`) in `{Package}/NullObjects/`
- **Test Fake**: `Fake{Noun}` (e.g., `FakeEmbeddingGenerator`) in `tests/NetIndex.Testing.Common/Fakes/`

### Method Naming

- **Async methods**: `{Action}Async` (enforced by VSTHRD200 analyzer)
  - Examples: `IngestAsync()`, `QueryAsync()`, `GenerateAsync()`, `ResetAsync()`
  - Never: `Ingest()`, `GetQuery()`, `Generate()`

- **CancellationToken parameter**: Always last
  ```csharp
  Task IngestAsync(IDocument document, CancellationToken cancellationToken = default);
  // Never: CancellationToken first or middle
  ```

### Package Naming

- Core: `NetIndex.Core.{Layer}`
  - `NetIndex.Core.Abstractions` (contracts)
  - `NetIndex.Core` (implementation)

- Feature areas: `NetIndex.{Category}.{Specificity}`
  - `NetIndex.Providers.Ollama` (provider)
  - `NetIndex.Storage.Sqlite` (storage)
  - `NetIndex.Ingestion.Pdf` (ingestion)
  - `NetIndex.AspNetCore` (middleware)

### Folder Structure

```
src/{Category}/
  {Package}/
    {Noun}.cs                  # Main type
    {Noun}Options.cs           # Configuration
    {Noun}Extensions.cs        # DI extension methods
    I{Noun}.cs                 # Interface (if not in Abstractions)
    Abstractions/
      (nested abstractions only)
    NullObjects/
      DenyAll{Noun}.cs
    Implementations/
      (concrete implementations)
```

## Consistency Checkpoints

Before committing, verify:

- [ ] All interfaces start with `I`
- [ ] All async methods end with `Async`
- [ ] CancellationToken is the last parameter
- [ ] No abbreviations in names
- [ ] NullObjects in `{Package}/NullObjects/`
- [ ] Test fakes in `tests/NetIndex.Testing.Common/Fakes/`
- [ ] Package names follow `NetIndex.{Category}.{Specificity}`
- [ ] No type names conflict with this registry

---

**Violations of this registry require consensus before implementation.**

Last Updated: 2026-04-28  
Status: ✅ FROZEN
