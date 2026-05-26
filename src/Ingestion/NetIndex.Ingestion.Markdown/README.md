# NetIndex.Ingestion.Markdown

Markdown document loader for NetIndex. Strips YAML front-matter and converts `.md` file content to plain text, producing `IDocument` instances ready for chunking and embedding.

```bash
dotnet add package NetIndex.Ingestion.Markdown
```

```csharp
services.AddNetIndex(builder => builder
    .UseMarkdownLoader()
    .Build());
```

[Full documentation and source →](https://github.com/M-Sahin/NetIndex#readme)
