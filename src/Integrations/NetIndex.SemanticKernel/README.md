# NetIndex.SemanticKernel

Semantic Kernel `KernelPlugin` adapter for NetIndex, exposing RAG retrieval, ingestion, and generation as agent-callable tools.

```bash
dotnet add package NetIndex.SemanticKernel
```

```csharp
using NetIndex.SemanticKernel;

kernel.Plugins.AddNetIndexPlugin(netindexPipeline);
```

## Functions

The plugin registers exactly three `[KernelFunction]` tools backed by `INetIndexPipeline`:

| Function | Arguments | Returns |
|---|---|---|
| `RetrieveChunks` | `query: string` | A list of `NetIndexRetrievedChunk` (chunk ID, document ID, text, score, metadata) |
| `IngestDocument` | `documentId: string`, `content: string` | A `NetIndexIngestionResult` with the ingested document ID |
| `GenerateAnswer` | `query: string` | The concatenated generated answer text |

Each function accepts an optional trailing `CancellationToken` supplied by Semantic Kernel; it is not advertised to the model.

## Notes

- The host application supplies its own Semantic Kernel model connector (chat completion, embeddings, etc.); this package adds no connector.
- All operations run through the configured `INetIndexPipeline`, so existing NetIndex authorization and tenant isolation apply unchanged.
- `IngestDocument` mutates the configured index. Expose it only to trusted agents and tool-selection policies.
- `RetrieveChunks` and `GenerateAnswer` materialize NetIndex's streaming results only at the plugin boundary; the underlying pipeline remains streaming.

[Full documentation and source →](https://github.com/M-Sahin/NetIndex#readme)
