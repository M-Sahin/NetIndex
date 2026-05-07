namespace NetIndex.Ingestion.Docx.Options;

/// <summary>
/// Configuration options for <see cref="Loaders.WordDocumentLoader"/>.
/// </summary>
public sealed class WordDocumentLoaderOptions
{
    /// <summary>
    /// Maximum input size, in bytes, that the loader will buffer for parsing.
    /// Streams that exceed this limit cause the load to fail with <see cref="System.IO.InvalidDataException"/>
    /// to mitigate ZIP-bomb / out-of-memory hazards. Default is 100 MiB.
    /// </summary>
    public long MaxInputSizeBytes { get; set; } = 100L * 1024 * 1024;
}
