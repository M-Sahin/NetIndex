# Contributing to NetIndex

Thank you for contributing to NetIndex! This document outlines our development process, code standards, and how to submit contributions.

## Open Core Scope

This repository contains the Apache-2.0 Core platform.

- Contributions here should target foundational RAG capabilities, extensibility, and developer experience.
- Enterprise-only capabilities (commercial packages/services) are maintained outside this repository.
- Public extension points for enterprise integrations are welcome in Core when broadly useful.

## Getting Started

### Prerequisites

- .NET 9 SDK (pinned in `global.json`)
- Git
- Code editor (VS Code, Visual Studio, or Rider)

### Setup

```bash
git clone https://github.com/murat/rag-pipeline-net.git
cd rag-pipeline-net
dotnet build NetIndex.sln
```

## Development Workflow

### Branch Naming

- `feature/short-name` — New features
- `fix/short-name` — Bug fixes
- `docs/short-name` — Documentation only
- `refactor/short-name` — Non-functional changes

### Code Standards

**Naming Conventions:**
- Interfaces: `I{Noun}` (e.g., `IVectorStore`)
- Async methods: `{Action}Async` (enforced via VSTHRD200 analyzer)
- NullObject implementations: `{Package}/NullObjects/{Name}` (e.g., `DenyAllTenantResolver`)
- Constants: `UPPER_CASE`

**Code Style:**
- Nullable reference types enabled (`<Nullable>enable</Nullable>`)
- Implicit usings enabled
- CancellationToken parameters must be last (CA1068 enforced)
- XML documentation for all public APIs
- Maximum line length: 120 characters

**See `.editorconfig` for all rules** — automatically enforced on build.

### Project Organization

- **Core**: `NetIndex.Core.Abstractions` (zero external dependencies)
- **Providers & Storage**: Reference only `Core.Abstractions`
- **AspNetCore**: References `Core.Abstractions` and `Core`
- **Integrations**: Reference only `Core.Abstractions` (e.g. `NetIndex.SemanticKernel`)
- **Tests**: One project per src project, use xUnit + NSubstitute

### Testing

**Test Traits** (used for test filtering):

- `[Trait("Category", "ArchContract")]` — Architecture conformance (runs on every PR)
- `[Trait("Category", "SecurityContract")]` — Security invariants (runs on every PR)
- `[Trait("Category", "PipelineContract")]` — Pipeline integration (runs on every PR)
- `[Trait("Category", "ContractTest")]` — Contract compliance (runs on main merge)
- `[Trait("Category", "Integration")]` — End-to-end integration (runs on main merge)
- `[Trait("Category", "Evaluation")]` — RAG quality metrics (nightly scheduled)
- `[Trait("Category", "Benchmark")]` — Performance profiling (manual only)

**Run tests locally:**

```bash
# All tests
dotnet test NetIndex.sln

# Only PR gate tests
dotnet test NetIndex.sln --filter "Category=ArchContract|Category=SecurityContract|Category=PipelineContract"

# Specific project
dotnet test tests/NetIndex.Core.Tests/NetIndex.Core.Tests.csproj
```

### Dependency Rules (Enforced)

```
Core.Abstractions → System.* only
        ↑
    Core
    AspNetCore
    Providers.* (Ollama, OpenAI, AzureOpenAI)
    Storage.* (InMemory, Sqlite, Pgvector)
    Ingestion.* (Pdf, Docx, Tesseract)
    Integrations.* (SemanticKernel)
```

Violations fail the PR gate. Use `NetArchTest.Rules` to verify in tests.

## Submitting Changes

### 1. Create a Feature Branch

```bash
git checkout -b feature/your-feature-name
```

### 2. Make Changes

- Write code following the standards above
- Add tests for new behavior
- Run `dotnet build NetIndex.sln` frequently
- Ensure all tests pass locally before pushing

### 3. Commit Message Format

```
{Type}: {Description}

{Detailed explanation if needed}

Fixes #{IssueNumber}
```

**Types**: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`, `perf`

Example:

```
feat: Add pgvector storage implementation

- Implement IVectorStore for PostgreSQL with pgvector extension
- Add dimension validation at Build() time
- Include integration tests with Testcontainers
- Update CI to include pgvector test collection

Fixes #123
```

### 4. Push and Open a PR

```bash
git push origin feature/your-feature-name
```

Then open a PR on GitHub with a clear description.

### 5. Code Review

- **PR gate checks**:
  - ✅ Builds with zero errors/warnings
  - ✅ All `ArchContract` tests pass
  - ✅ All `SecurityContract` tests pass
  - ✅ All `PipelineContract` tests pass
  - ✅ No circular dependencies

- **Review process**:
  - At least 1 approval from maintainers
  - All conversations resolved
  - CI checks green

### 6. Merge

Once approved, a maintainer will squash-merge your PR.

## Architecture & Design

**Key Principles:**

1. **Zero External Dependencies in Abstractions** — `Core.Abstractions` only references `System.*`
2. **Acyclic Dependency Graph** — Enforced by `NetArchTest` on every PR
3. **Contract-Based Testing** — `VectorStoreContractSuite` ensures all implementations meet the contract
4. **Async-First** — All I/O operations are async with proper `CancellationToken` propagation
5. **Structured Error Handling** — Typed exception hierarchy with retry semantics
6. **Security by Default** — Deny-all auth unless explicitly configured
7. **Observable** — Structured logging + OpenTelemetry tracing throughout

## Common Tasks

### Add a New Vector Store Implementation

1. Create `src/Storage/NetIndex.Storage.MyStore/`
2. Implement `IVectorStore` interface
3. Create test project `tests/NetIndex.Storage.MyStore.Tests/`
4. Inherit from `VectorStoreContractSuite` to validate contract
5. Add `.csproj` entries to `NetIndex.sln`
6. Update CI configuration in `.github/workflows/`

### Add a New Provider (Embedding or Chat)

1. Create `src/Providers/NetIndex.Providers.MyProvider/`
2. Implement `IEmbeddingGenerator` or `IChatClient`
3. Follow naming: `INetIndexBuilder.UseMyProvider(...)`
4. Create test project similarly
5. Reference only `Core.Abstractions`

### Update Documentation

1. Edit relevant `.md` files
2. Ensure code examples are correct
3. If architecture changes, update `docs/architecture.md`

## Questions?

- **GitHub Issues** — For bugs and feature requests
- **GitHub Discussions** — For general questions and ideas
- **Email** — See [SECURITY.md](SECURITY.md) for security-related inquiries

---

**Thank you for contributing to NetIndex!**
