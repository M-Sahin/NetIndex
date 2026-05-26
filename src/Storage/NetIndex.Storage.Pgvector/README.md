# NetIndex.Storage.Pgvector

PostgreSQL pgvector storage backend for NetIndex. Supports HNSW indexing, multi-tenant chunk isolation, and SourceLink-debuggable async operations against any PostgreSQL 15+ instance.

```bash
dotnet add package NetIndex.Storage.Pgvector
```

```csharp
services.AddNetIndex(builder => builder
    .UsePgvector(o => o.ConnectionString = "Host=localhost;Database=rag;", dimensions: 768)
    .Build());
```

[Full documentation and source →](https://github.com/M-Sahin/NetIndex#readme)
