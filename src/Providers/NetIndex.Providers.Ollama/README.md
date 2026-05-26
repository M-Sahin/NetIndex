# NetIndex.Providers.Ollama

Ollama embedding and streaming chat provider for NetIndex. Enables fully local LLM inference with models such as `llama3`, `nomic-embed-text`, and others — no cloud account required.

```bash
dotnet add package NetIndex.Providers.Ollama
```

```csharp
services.AddNetIndex(builder => builder
    .UseOllama(o => { o.BaseUrl = "http://localhost:11434"; o.EmbeddingModel = "nomic-embed-text"; })
    .Build());
```

[Full documentation and source →](https://github.com/M-Sahin/NetIndex#readme)
