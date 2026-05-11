namespace NetIndex.Storage.Sqlite.Options;

/// <summary>Options for the SQLite vector store backed by sqlite-vec.</summary>
public sealed class SqliteOptions
{
    /// <summary>Gets or sets the SQLite connection string. Default: file-based <c>netindex.db</c>.</summary>
    public string ConnectionString { get; set; } = "Data Source=./netindex.db";

    /// <summary>
    /// Gets or sets the expected embedding dimensions.
    /// Must match the configured <see cref="NetIndex.Core.Abstractions.IEmbeddingGenerator.Dimensions"/>.
    /// </summary>
    public int Dimensions { get; set; } = 384;
}
