# NetIndex.Core.Abstractions

Core contracts and interfaces for the NetIndex RAG framework: `IEmbeddingGenerator`, `IVectorStore`, `IChatClient`, and `ITenantResolver`. Reference this package to implement custom providers or storage backends without taking a dependency on the full framework.

```bash
dotnet add package NetIndex.Core.Abstractions
```

```csharp
// Implement a custom vector store
public class MyVectorStore : IVectorStore { ... }
```

[Full documentation and source →](https://github.com/M-Sahin/NetIndex#readme)
