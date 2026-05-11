namespace NetIndex.Storage.InMemory.Options;

/// <summary>
/// Options for the in-memory vector store.
/// </summary>
public sealed class InMemoryOptions
{
    /// <summary>
    /// Gets or sets the expected embedding dimensions. Default: 384.
    /// </summary>
    public int Dimensions { get; set; } = 384;
}