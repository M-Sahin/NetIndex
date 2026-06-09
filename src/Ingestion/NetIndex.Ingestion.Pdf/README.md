# NetIndex.Ingestion.Pdf

PDF document loader for NetIndex using PdfPig. Extracts text page-by-page from `.pdf` files and surfaces them as `IDocument` instances ready for chunking and embedding.

```bash
dotnet add package NetIndex.Ingestion.Pdf
```

```csharp
services.AddNetIndex(builder => builder
    .UsePdfDocumentLoader()
    .Build());
```

[Full documentation and source →](https://github.com/M-Sahin/NetIndex#readme)
