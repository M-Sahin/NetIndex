# NetIndex.Providers.AzureOpenAI

Azure OpenAI embedding and chat-completion provider for NetIndex. Supports `DefaultAzureCredential` managed-identity auth, per-request activity tracing, and configurable deployment names.

```bash
dotnet add package NetIndex.Providers.AzureOpenAI
```

```csharp
services.AddNetIndex(builder => builder
    .UseAzureOpenAI(o => {
        o.Endpoint = "https://my-resource.openai.azure.com/";
        o.EmbeddingDeployment = "text-embedding-3-large";
        o.ChatDeployment = "gpt-4o";
    }).Build());
```

[Full documentation and source →](https://github.com/M-Sahin/NetIndex#readme)
