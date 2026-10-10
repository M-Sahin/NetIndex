# Changelog

All notable changes to NetIndex will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- Adopted Open Core distribution strategy.
- Core repository license changed to Apache-2.0.
- Defined enterprise-only capabilities as commercial add-ons (RBAC, compliance auditing, managed hosting/UI, priority support).

## [0.9.4] - 2026-10-10

### Added

- `NetIndex.Providers.TextEmbeddingsInference`: an `IDocumentReranker` over the Text Embeddings Inference `POST /rerank` API (`UseTeiReranker`). Results are reordered by the cross-encoder score (ties keep input order) and carry that score; zero or one result is returned without a call.
- `TeiRerankerOptions`: `Endpoint`, `ConnectTimeout` (500 ms), `RequestTimeout` (5 s), `FailureThreshold` (3) and `BreakDuration` (30 s), validated at build.
- A hand-written circuit breaker (closed, open, half-open). While open, calls throw a retryable `TeiCircuitOpenException` without any network attempt. Failures surface as `NetIndexProviderException`; no `HttpRequestException` leaks.

## [0.9.3] - 2026-10-04

### Fixed

- Configured chunk sizes are now honoured. `UseChunking(...)` registers the configured `ChunkingOptions` and the pipeline passes them to the strategy; the pipeline previously always used 1000 / 200 / `"\n\n"`. Pipelines without `UseChunking` keep that fallback.
- No chunking strategy (fixed-size, semantic, recursive) emits a chunk longer than `ChunkSize` any more: oversized text is split at sentence, then whitespace, then hard character boundaries, with overlap preserved. Sizes are enforced as about 4 characters per token.
- Re-ingesting a shorter document no longer leaves stale chunks retrievable. `NetIndexPipeline.IngestAsync` now replaces the document's chunk set instead of upserting over it.
- Re-ingesting a document id held by another tenant is rejected with `NetIndexAuthorizationException` (`FailureReason = "CrossTenantDocumentCollision"`); nothing is written or deleted. Previously the other tenant's chunks were silently overwritten. A new chunk id that already exists under a different document or tenant is rejected the same way.
- `NetIndexPipeline.IngestAsync` now rejects a blank document id, a document with empty or whitespace-only content, or one that yields no non-blank chunks, with an `ArgumentException` before any store call; nothing is written or deleted. Removing a document stays an explicit `DeleteAsync`.
- `Semantic()` and `Recursive()` now validate their sizes like `FixedSize()` (size above zero and at most `int.MaxValue / 4`, overlap from zero up to below the size). An overlap that is not below the chunk size now throws instead of being silently reset to zero.
- The recursive strategy's semantic stage is reachable again and its output is normalized (fresh chunk ids, no embedding, `pending` document id, no metadata).

### Changed

- Breaking (pre-1.0): custom `IVectorStore` implementations used with the pipeline must implement `ReplaceDocumentAsync`. The default implementation fails closed with `NetIndexConfigurationException` and writes and deletes nothing.
- New public members: `IVectorStore.ReplaceDocumentAsync(documentId, chunks, ct)`, the static helpers `IVectorStore.ValidateReplacement` and `IVectorStore.CrossTenantCollision`, the constant `IVectorStore.CROSS_TENANT_DOCUMENT_COLLISION`, the `UseChunking()` no-argument overload, and the 9-argument `NetIndexPipeline` constructor taking `ChunkingOptions?`.
- The pgvector, SQLite and both in-memory stores override `ReplaceDocumentAsync` with one transaction or lock, which queries respect, and enforce the tenant check inside it. A mixed-tenant, foreign-document or duplicate-chunk-id replacement set throws `ArgumentException`.
- Document ids are one global namespace across tenants (chunk ids are `{documentId}_chunk_{i}`); multi-tenant hosts must mint opaque, tenant-unique document ids. Only `ReplaceDocumentAsync` (and therefore `IngestAsync`) enforces the cross-tenant check; `UpsertAsync` and `DeleteAsync` do not.
- Behaviour change: callers of `UseChunking()` or `UseChunking(c => ...)` now get the configured size, overlap and separator (defaults 512 / 64 / `"\n"`) instead of the forced 1000 / 200 / `"\n\n"`.
- `PgvectorVectorStore` now creates `idx_rag_chunks_document_id` on `rag_chunks (document_id)` at initialization, so replace and delete no longer scan the table.
- `SqliteVectorStore.QueryAsync` now shares the write lock so it never reads uncommitted state on the shared connection; queries no longer run concurrently with writes.

## [1.0.0] - 2026-Q3

### Added

- ✅ Core abstractions: `INetIndexBuilder`, `IVectorStore`, `IEmbeddingGenerator`, `IChatClient`
- ✅ Document ingestion: PDF, DOCX, Markdown support
- ✅ Local RAG: Ollama embeddings + SQLite vector storage
- ✅ Enterprise cloud: Azure OpenAI + pgvector
- ✅ Multi-tenancy: RBAC with claim-based filtering
- ✅ Observability: Structured logging + OpenTelemetry tracing
- ✅ Developer template: `dotnet new netindex`
- ✅ Zero-config defaults: `AddNetIndex()` with deny-all auth
- ✅ Ecosystem: Semantic Kernel plugin + SK integration
- ✅ RAG evaluation: Retrieval relevance + answer faithfulness metrics

### Security

- Deny-all authorization by default
- Fail-fast dimension mismatch validation
- Structured exception hierarchy with retry semantics

## [0.9.0] - 2026-Q2

### Added

- Repository scaffolding and build infrastructure
- Core abstractions frozen (interfaces, types, contracts)
- xUnit test suite with contract testing framework
- GitHub Actions CI/CD workflows

---

**NetIndex — Enterprise RAG for .NET 9**
