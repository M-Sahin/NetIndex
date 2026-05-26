# NetIndex.Providers.OpenAI

OpenAI embedding and chat-completion provider for NetIndex, supporting `text-embedding-3-large` and `gpt-4o` model families via a direct API key.

```bash
dotnet add package NetIndex.Providers.OpenAI
```

```csharp
services.AddNetIndex(builder => builder
    .UseOpenAI(o => { o.ApiKey = "sk-..."; o.ChatModel = "gpt-4o"; })
    .Build());
```

[Full documentation and source →](https://github.com/M-Sahin/NetIndex#readme)
