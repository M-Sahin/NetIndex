# NetIndex.Ingestion.Tesseract

Tesseract OCR document loader for NetIndex. Extracts text from images and scanned documents via the Tesseract engine, producing `IDocument` instances ready for chunking and embedding.

```bash
dotnet add package NetIndex.Ingestion.Tesseract
```

```csharp
services.AddNetIndex(builder => builder
    .UseTesseractLoader(o => o.TessDataPath = "/usr/share/tessdata")
    .Build());
```

[Full documentation and source →](https://github.com/M-Sahin/NetIndex#readme)
