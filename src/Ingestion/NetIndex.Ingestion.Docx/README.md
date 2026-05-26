# NetIndex.Ingestion.Docx

Word document (.docx) loader for NetIndex using Open XML SDK. Extracts paragraph text from `.docx` files and surfaces them as `IDocument` instances ready for chunking and embedding.

```bash
dotnet add package NetIndex.Ingestion.Docx
```

```csharp
services.AddNetIndex(builder => builder
    .UseDocxLoader()
    .Build());
```

[Full documentation and source →](https://github.com/M-Sahin/NetIndex#readme)
