# NetIndex.Ingestion

Document chunking strategies and ingestion pipeline extensions for NetIndex. Provides sliding-window and sentence-boundary chunking via `IChunkingStrategy`, wired through the standard `INetIndexBuilder` fluent API.

```bash
dotnet add package NetIndex.Ingestion
```

```csharp
services.AddNetIndex(builder => builder
    .UseSlidingWindowChunking(chunkSize: 512, overlap: 64)
    .Build());
```

[Full documentation and source →](https://github.com/M-Sahin/NetIndex#readme)
