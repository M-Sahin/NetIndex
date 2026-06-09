# NetIndex.Ingestion.Tesseract

Tesseract OCR support for NetIndex. Adds scanned-PDF ingestion by rasterizing pages with PDFtoImage and recognising text with TesseractOCR — without adding native dependencies to applications that do not need OCR.

```bash
dotnet add package NetIndex.Ingestion.Tesseract
```

## Quick start

```csharp
services.AddNetIndex(builder => builder
    .UsePdfDocumentLoader()
    .UseTesseract(o =>
    {
        o.TessDataPath = "/usr/share/tessdata";  // required
        o.Languages    = "eng";                  // default
        o.RasterizationDpi = 300;               // default; valid range 72–600
    })
    .Build());
```

`UsePdfDocumentLoader` registers the PDF loader. `UseTesseract` registers the OCR extractor and validator. When `PdfDocumentLoader` detects a scanned page (text below `MinimumTextPerPageThreshold`), it automatically delegates to `IVisionExtractor`.

## Prerequisites

### Linux (glibc x64)

```bash
sudo apt-get install -y tesseract-ocr libtesseract-dev libleptonica-dev
sudo ldconfig
```

Confirm loader aliases are in place:

```bash
ldconfig -p | grep -E 'libtess|liblept'
```

### Windows x64

The TesseractOCR NuGet package bundles the required DLLs. Install the **Visual C++ 2022 Redistributable** if not already present.

### macOS / musl (Alpine) / ARM64

Unsupported or experimental. Contributions welcome.

## Tessdata provisioning

Download `eng.traineddata` (or other language data) from [tessdata_fast](https://github.com/tesseract-ocr/tessdata_fast) and place it in `TessDataPath`. The validator checks that the file exists at startup without loading any native binary.

```bash
mkdir -p /usr/share/tessdata
curl -fsSL https://github.com/tesseract-ocr/tessdata_fast/raw/4.0.0/eng.traineddata \
     -o /usr/share/tessdata/eng.traineddata
```

## Options

| Option | Default | Description |
|---|---|---|
| `TessDataPath` | *(required)* | Path to the directory containing `.traineddata` files |
| `Languages` | `eng` | Language code(s); combine with `+` (e.g., `eng+fra`) |
| `RasterizationDpi` | `300` | PDF render DPI (72–600) |
| `MaxInputBytes` | 50 MB | Maximum input stream size |
| `MaxPages` | `100` | Maximum pages to process per document |
| `MaxPixelsPerPage` | 50,000,000 | Maximum pixels per rendered page |

Repeated `.UseTesseract()` calls are idempotent: only one validator and one extractor singleton are registered. Later `configure` delegates win for each property.

## OCR metadata

When a PDF is processed via OCR, `PdfDocument.Metadata` contains:

| Key | Description |
|---|---|
| `ocr_engine` | `tesseract` |
| `ocr_engine_version` | Runtime Tesseract version |
| `ocr_language` | Configured language(s) |
| `ocr_mean_confidence` | Document-level mean confidence (0–1, invariant culture) |
| `ocr_page_count` | Number of pages processed |
| `ocr_dpi` | Render DPI used |

## Error handling

| Scenario | Exception |
|---|---|
| Native library missing | `NetIndexOcrNotInstalledException` |
| Invalid tessdata path/files | `NetIndexConfigurationException` (at `Build()` time) |
| Render or recognition failure | `NetIndexProviderException` (`ProviderName = "Tesseract"`) |
| Unsupported media type | `NetIndexProviderException` (`ErrorCode = "unsupported_media_type"`) |
| Empty OCR output | `NetIndexProviderException` (`ErrorCode = "empty_ocr_result"`) |

## Supported platforms

| Platform | Status |
|---|---|
| Windows x64 | Supported |
| glibc Linux x64 | Supported |
| macOS | Experimental (untested) |
| musl/Alpine | Unsupported |
| Windows ARM64 | Unsupported |
| Linux ARM64 | Experimental (untested) |

[Full documentation and source →](https://github.com/M-Sahin/NetIndex#readme)
