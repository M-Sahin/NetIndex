# NetIndex.Storage.Sqlite

SQLite vector store for NetIndex backed by sqlite-vec. Provides zero-dependency local persistence with cosine-similarity search — no separate database process required.

```bash
dotnet add package NetIndex.Storage.Sqlite
```

```csharp
services.AddNetIndex(builder => builder
    .UseSqlite("Data Source=rag.db;", dimensions: 768)
    .Build());
```

[Full documentation and source →](https://github.com/M-Sahin/NetIndex#readme)
