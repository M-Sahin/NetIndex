# NetIndex.Providers.TextEmbeddingsInference

Cross-encoder reranker for NetIndex. Implements `IDocumentReranker` over the Hugging Face
[Text Embeddings Inference](https://github.com/huggingface/text-embeddings-inference) `POST /rerank` API,
so a local sidecar can reorder retrieved chunks without any cloud dependency.

```bash
dotnet add package NetIndex.Providers.TextEmbeddingsInference
```

```csharp
services.AddNetIndex(builder => builder
    .UseTeiReranker(o => o.Endpoint = "http://localhost:8080")
    .Build());
```

Behaviour:

- Results are reordered by descending reranker score (ties keep input order). Each returned
  `SearchResult` carries the reranker score.
- Zero or one result is returned unchanged, without a network call.
- Failures surface as `NetIndexProviderException` (retryable for connection errors, timeouts, 429 and 5xx);
  no `HttpRequestException` leaks. The caller decides whether to fall back.
- A hand-written circuit breaker (`FailureThreshold` consecutive failures open it for `BreakDuration`) rejects
  calls with a retryable `TeiCircuitOpenException` and makes no network attempt while open; one probe is let
  through when the break elapses.
- `ConnectTimeout` (default 500 ms) and `RequestTimeout` (default 5 s) are configurable.

[Full documentation and source →](https://github.com/M-Sahin/NetIndex#readme)
