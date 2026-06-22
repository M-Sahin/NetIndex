# NetIndex.Providers.OpenAI

Standard OpenAI embedding and chat-completion provider for NetIndex. Use this package for the official OpenAI API or any OpenAI-compatible HTTPS endpoint. For Azure OpenAI deployments (managed identity, RBAC), use `NetIndex.Providers.AzureOpenAI` instead.

## Installation

```bash
dotnet add package NetIndex.Providers.OpenAI
```

## Quick start

### Inline configuration

```csharp
services.AddNetIndex(builder => builder
    .UseOpenAI(opts =>
    {
        opts.ApiKey = "sk-...";        // required — use secrets, never hardcode
    })
    .UseSqlite("Data Source=vectors.db")
    .Build());
```

### Configuration section

```json
{
  "OpenAI": {
    "ApiKey": "sk-...",
    "EmbeddingModel": "text-embedding-3-small",
    "ChatModel": "gpt-4o-mini"
  }
}
```

```csharp
services.AddNetIndex(builder => builder
    .UseOpenAI(Configuration.GetSection("OpenAI"))
    .UsePgvector(Configuration.GetConnectionString("Postgres"))
    .Build());
```

## Options

| Option | Default | Description |
|--------|---------|-------------|
| `ApiKey` | — | **Required.** Your `sk-...` API key. Use environment variables or a secret manager, never source code. |
| `Endpoint` | `https://api.openai.com/v1` | Optional custom HTTPS endpoint for OpenAI-compatible services. Must be absolute HTTPS. |
| `EmbeddingModel` | `text-embedding-3-small` | Embedding model name. See dimension table below. |
| `ChatModel` | `gpt-4o-mini` | Chat completion model name. |
| `EmbeddingDimensions` | _(inferred)_ | Optional override for embedding-v3 dimension shortening. Must be positive. |
| `Timeout` | 100 s | Network timeout applied to SDK requests. |

## Embedding dimensions

Dimensions are inferred automatically for known models. Unknown models require `EmbeddingDimensions` to be set explicitly.

| Model | Native dimensions |
|-------|-------------------|
| `text-embedding-3-small` | 1536 |
| `text-embedding-3-large` | 3072 |
| `text-embedding-ada-002` | 1536 |

`EmbeddingDimensions` overrides the native dimension for embedding-v3 shortening. The `NetIndexBuilder.Build()` call validates dimension parity between this provider and the configured vector store — a mismatch throws `NetIndexConfigurationException` at startup.

## Cancellation and exceptions

- **Caller cancellation** is rethrown as `OperationCanceledException`, never wrapped.
- **HTTP errors** surface as `NetIndexProviderException`. HTTP 429/5xx are retryable; 4xx (except 408) are non-retryable. 401/403 surface as `OpenAIAuthenticationException`.
- **SDK timeout** (internal, not caller) surfaces as retryable `NetIndexProviderException` with `ErrorCode = "timeout"`.
- **Network I/O errors** surface as retryable `NetIndexProviderException` with `ErrorCode = "network"`.
- Both provider classes implement `IAsyncDisposable` and are idempotent on double dispose.

## Custom endpoint (OpenAI-compatible services)

```csharp
services.AddNetIndex(builder => builder
    .UseOpenAI(opts =>
    {
        opts.ApiKey = "local-key";
        opts.Endpoint = new Uri("https://my-compat-llm.example.com/v1/");
        opts.EmbeddingModel = "my-embed-model";
        opts.EmbeddingDimensions = 768;
        opts.ChatModel = "my-chat-model";
    })
    .Build());
```

[Full documentation and source →](https://github.com/M-Sahin/NetIndex#readme)
