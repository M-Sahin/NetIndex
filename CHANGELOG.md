# Changelog

All notable changes to NetIndex will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

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
