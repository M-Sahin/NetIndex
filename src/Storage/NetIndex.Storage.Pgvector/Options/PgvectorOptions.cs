namespace NetIndex.Storage.Pgvector.Options;

/// <summary>Options for the pgvector-backed vector store.</summary>
public sealed class PgvectorOptions
{
    /// <summary>
    /// Gets or sets the PostgreSQL connection string.
    /// Default is an empty string; must be set explicitly before the store is used.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the expected embedding dimension count.
    /// Must match the configured <see cref="NetIndex.Core.Abstractions.IEmbeddingGenerator.Dimensions"/>.
    /// Default is <c>1536</c>, matching the Azure OpenAI <c>text-embedding-3-small</c> default.
    /// </summary>
    public int Dimensions { get; set; } = 1536;
}
