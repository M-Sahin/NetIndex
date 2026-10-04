# Changelog

All notable changes to NetIndex will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- `NetIndexPipeline.IngestAsync` now rejects a document with empty or whitespace-only content, or one that chunks to zero chunks, with an `ArgumentException` before any store call; nothing is written or deleted. Removing a document stays an explicit `DeleteAsync`.
- Configured chunk sizes are now honoured. `UseChunking(...)` registers the configured `ChunkingOptions` and the pipeline passes them to the strategy; the pipeline previously always used 1000 / 200 / `"\n\n"`. Pipelines without `UseChunking` keep that fallback.
- No chunking strategy (fixed-size, semantic, recursive) emits a chunk longer than `ChunkSize` any more: oversized text is split at sentence, then whitespace, then hard character boundaries, with overlap preserved.
- Re-ingesting a shorter document no longer leaves stale chunks retrievable. `NetIndexPipeline.IngestAsync` now replaces the document's chunk set instead of upserting over it.
- Re-ingesting a document id held by another tenant is rejected with `NetIndexAuthorizationException` (`FailureReason = "CrossTenantDocumentCollision"`); nothing is written or deleted. Previously the other tenant's chunks were silently overwritten.

### Changed

- `IVectorStore.ReplaceDocumentAsync(documentId, chunks, ct)` added as a default interface method (delete-then-upsert, documented as not atomic, no tenant check). The pgvector, SQLite and both in-memory stores override it with one transaction or lock, which queries respect, and enforce the tenant check inside it. Custom stores should override it.
- Behaviour change: callers of `UseChunking()` (new no-argument overload) or `UseChunking(c => ...)` now get the configured size, overlap and separator (defaults 512 / 64 / `"\n"`) instead of the forced 1000 / 200 / `"\n\n"`.
- `SqliteVectorStore.QueryAsync` now takes the write lock so it never reads uncommitted state on the shared connection.
- Adopted Open Core distribution strategy.
- Core repository license changed to Apache-2.0.
- Defined enterprise-only capabilities as commercial add-ons (RBAC, compliance auditing, managed hosting/UI, priority support).

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
