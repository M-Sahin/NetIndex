# NetIndex.Storage.InMemory

In-memory vector store for NetIndex. Stores embeddings in a concurrent dictionary with cosine-similarity search — ideal for tests, demos, and single-process workloads that do not require persistence.

```bash
dotnet add package NetIndex.Storage.InMemory
```

```csharp
services.AddNetIndex(builder => builder
    .UseInMemoryVectorStore(dimensions: 768)
    .Build());
```

[Full documentation and source →](https://github.com/M-Sahin/NetIndex#readme)
