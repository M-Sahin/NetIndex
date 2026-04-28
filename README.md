# NetIndex — LlamaIndex for .NET

Enterprise-ready, open-source Retrieval-Augmented Generation (RAG) framework for C#/.NET 9. Build production-grade AI-powered applications with vector embeddings, semantic search, and multi-tenant support.

## Quick Start

### Local Development (Ollama + SQLite)

```bash
# Prerequisites
# - .NET 9 SDK
# - Ollama running locally (ollama serve)

dotnet new netindex --name MyRagApp
cd MyRagApp
dotnet run
```

### Azure Enterprise (Azure OpenAI + pgvector)

```csharp
services.AddNetIndex(builder => builder
    .UseAzureOpenAI(opts => opts.Endpoint = "https://...openai.azure.com/")
    .UsePgvector("Host=db.example.com;..."));
```

## Features

- ✅ **Document Ingestion**: PDF, DOCX, Markdown, with optional OCR
- ✅ **Vector Embeddings**: Ollama (local), OpenAI, Azure OpenAI
- ✅ **Vector Storage**: In-memory, SQLite (local), pgvector (PostgreSQL)
- ✅ **Semantic Search**: Built-in similarity search with relevance scoring
- ✅ **LLM Integration**: Chat completion streaming via standard APIs
- ✅ **Extensible Security Hooks**: Pluggable tenant resolver and authorization extension points
- ✅ **Core Observability**: Structured logging and distributed tracing (OpenTelemetry)
- ✅ **Zero-Config Defaults**: Works out-of-the-box with deny-all auth
- ✅ **Ecosystem**: Semantic Kernel integration, RAG evaluation harness

## Open Core Model

NetIndex follows an Open Core model.

- **This repository (Core)** is Apache-2.0 licensed and includes the foundational RAG pipeline.
- **Enterprise add-ons** are commercially licensed packages and services.

### What is free in Core

- Pipeline orchestration and contracts
- Core ingestion, embedding, storage, and retrieval primitives
- Local and cloud provider adapters
- Basic tracing and logging
- ASP.NET Core integration hooks

### What is paid in Enterprise

- RBAC plugin for document/chunk-level authorization with ASP.NET Core Identity and AD mapping
- Advanced telemetry and audit dashboard (prompt-answer lineage and hallucination tracking)
- Managed hosting and ChatGPT-style web portal
- Compliance and governance packs (policy controls, audit retention, export)
- Priority support contracts with SLA

## Project Structure

```
src/
  Core/
    NetIndex.Core.Abstractions/     # Interfaces, contracts, zero external dependencies
    NetIndex.Core/                  # Pipeline orchestration, core implementations
  AspNetCore/
    NetIndex.AspNetCore/            # ASP.NET Core middleware, hosted services
  Providers/
    NetIndex.Providers.Ollama/      # Local LLM (OllamaSharp)
    NetIndex.Providers.OpenAI/      # OpenAI API
    NetIndex.Providers.AzureOpenAI/ # Azure OpenAI with DefaultAzureCredential
  Storage/
    NetIndex.Storage.InMemory/      # In-memory vectors (testing)
    NetIndex.Storage.Sqlite/        # SQLite with sqlite-vec
    NetIndex.Storage.Pgvector/      # PostgreSQL with pgvector extension
  Ingestion/
    NetIndex.Ingestion.Pdf/         # PDF parsing (iTextSharp)
    NetIndex.Ingestion.Docx/        # DOCX/Office Open XML
    NetIndex.Ingestion.Tesseract/   # OCR for scanned documents

tests/                              # One test project per src project
benchmarks/
  NetIndex.Benchmarks/              # Performance profiling (BenchmarkDotNet)
```

## Documentation

- **[Architecture](docs/architecture.md)** — Design decisions, dependency graph, patterns
- **[Contributing](CONTRIBUTING.md)** — Development guide, PR process
- **[Security](SECURITY.md)** — Vulnerability reporting, security policies

## Roadmap

**V1 (2026)** — Local RAG + Cloud Integration
- Foundation: Core abstractions, Ollama, SQLite, basic auth
- Enterprise: Azure OpenAI, pgvector, tenant isolation, tracing
- Ecosystem: SK integration, evaluation harness

**V2 (2026+)** — Advanced Agents & Optimization
- Multi-stage retrieval, reranking, HyDE
- Hybrid search (dense + sparse)
- Agent orchestration patterns
- Performance optimization

## License

Apache-2.0 for Core — See [LICENSE](LICENSE) for details.

Enterprise add-ons and managed hosting are offered under separate commercial terms.

## Support

- **Issues & Feature Requests** — [GitHub Issues](https://github.com/murat/rag-pipeline-net/issues)
- **Discussions** — [GitHub Discussions](https://github.com/murat/rag-pipeline-net/discussions)
- **Security Issues** — See [SECURITY.md](SECURITY.md) for responsible disclosure

---

**NetIndex — Bring LlamaIndex simplicity to .NET 9.**
