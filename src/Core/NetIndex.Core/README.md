# NetIndex.Core

Core pipeline orchestration for the NetIndex RAG framework. Wires `IEmbeddingGenerator`, `IVectorStore`, `IChatClient`, and `IChunkingStrategy` into the `INetIndexPipeline` ingest → query → generate lifecycle.

```bash
dotnet add package NetIndex.Core
```

```csharp
services.AddNetIndex(builder => builder
    .UseOllama()
    .UseSqlite("Data Source=rag.db;")
    .Build());
```

[Full documentation and source →](https://github.com/M-Sahin/NetIndex#readme)
