using NetIndex.Core.Abstractions;

namespace NetIndex.Testing.Common;

/// <summary>
/// Abstract xUnit test suite covering the full <see cref="IVectorStore"/> contract.
/// </summary>
/// <remarks>
/// Inherit this class and provide a concrete <see cref="Store"/> to validate any
/// vector store implementation against the canonical contract.
/// </remarks>
public abstract class VectorStoreContractSuite : IAsyncLifetime
{
    /// <summary>
    /// Override to provide the concrete <see cref="IVectorStore"/> under test.
    /// </summary>
    protected abstract IVectorStore Store { get; }

    /// <summary>
    /// Override when a concrete store reports dimension mismatches via a different exception subtype.
    /// </summary>
    protected virtual Type DimensionMismatchExceptionType => typeof(NetIndexConfigurationException);

    /// <summary>
    /// Creates the store in a clean state before each test.
    /// </summary>
    public virtual Task InitializeAsync()
    {
        // Override if setup is needed
        return Task.CompletedTask;
    }

    /// <summary>
    /// Cleans up after each test.
    /// </summary>
    public virtual Task DisposeAsync()
    {
        // Override if teardown is needed
        return Task.CompletedTask;
    }

    /// <summary>
    /// Generates a deterministic test embedding of the store's dimension count.
    /// </summary>
    private static float[] CreateVector(int dimensions, params float[] components)
    {
        var vector = new float[dimensions];
        var length = Math.Min(dimensions, components.Length);
        for (var index = 0; index < length; index++)
        {
            vector[index] = components[index];
        }

        var magnitude = Math.Sqrt(vector.Sum(component => component * component));
        if (magnitude <= 0)
        {
            vector[0] = 1f;
            return vector;
        }

        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] /= (float)magnitude;
        }

        return vector;
    }

    private static RagChunk CreateChunk(string chunkId, string documentId, float[] embedding)
        => new(chunkId, $"text-{chunkId}", embedding, documentId, null);

    private static async Task<IReadOnlyList<SearchResult<RagChunk>>> ReadAllAsync(
        IAsyncEnumerable<SearchResult<RagChunk>> source,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchResult<RagChunk>>();
        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            results.Add(item);
        }

        return results;
    }

    private async Task AssertDimensionMismatchAsync(Func<Task> action)
    {
        var exception = await Record.ExceptionAsync(action).ConfigureAwait(false);
        Assert.NotNull(exception);
        Assert.IsAssignableFrom(DimensionMismatchExceptionType, exception);
    }

    [Fact]
    public async Task Can_UpsertAndQuery_SingleDocumentAsync()
    {
        // Arrange
        var dimensions = Store.Dimensions;
        var chunk = CreateChunk("chunk-1", "document-1", CreateVector(dimensions, 1f, 0f, 0f));
        var queryVector = CreateVector(dimensions, 1f, 0f, 0f);

        // Act
        await Store.UpsertAsync(new[] { chunk }, CancellationToken.None);
        var results = await ReadAllAsync(Store.QueryAsync(queryVector, top: 1, CancellationToken.None), CancellationToken.None);

        // Assert
        Assert.Single(results);
        Assert.Equal("chunk-1", results[0].Item.Id);
    }

    [Fact]
    public async Task Can_UpsertAndQuery_MultipleDocumentsAsync()
    {
        // Arrange
        var dimensions = Store.Dimensions;
        var chunks = new[]
        {
            CreateChunk("chunk-1", "document-1", CreateVector(dimensions, 1f, 0f, 0f)),
            CreateChunk("chunk-2", "document-2", CreateVector(dimensions, 0.9f, 0.1f, 0f)),
            CreateChunk("chunk-3", "document-3", CreateVector(dimensions, 0.1f, 0.9f, 0f)),
        };
        var queryVector = CreateVector(dimensions, 1f, 0f, 0f);

        // Act
        await Store.UpsertAsync(chunks, CancellationToken.None);
        var results = await ReadAllAsync(Store.QueryAsync(queryVector, top: 2, CancellationToken.None), CancellationToken.None);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, result => result.Item.DocumentId == "document-1");
        Assert.Contains(results, result => result.Item.DocumentId == "document-2");
    }

    [Fact]
    public async Task Query_ReturnsResults_OrderedByRelevanceAsync()
    {
        // Arrange
        var dimensions = Store.Dimensions;
        var chunks = new[]
        {
            CreateChunk("chunk-1", "document-1", CreateVector(dimensions, 1f, 0f, 0f)),
            CreateChunk("chunk-2", "document-2", CreateVector(dimensions, 0.8f, 0.2f, 0f)),
            CreateChunk("chunk-3", "document-3", CreateVector(dimensions, 0.6f, 0.4f, 0f)),
        };
        var queryVector = CreateVector(dimensions, 1f, 0f, 0f);

        // Act
        await Store.UpsertAsync(chunks, CancellationToken.None);
        var results = await ReadAllAsync(Store.QueryAsync(queryVector, top: 3, CancellationToken.None), CancellationToken.None);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal("chunk-1", results[0].Item.Id);
        Assert.Equal("chunk-2", results[1].Item.Id);
        Assert.Equal("chunk-3", results[2].Item.Id);
        Assert.True(results[0].Score >= results[1].Score);
        Assert.True(results[1].Score >= results[2].Score);
    }

    [Fact]
    public async Task Delete_RemovesDocumentsFromResultsAsync()
    {
        // Arrange
        var dimensions = Store.Dimensions;
        var chunk = CreateChunk("chunk-delete", "document-delete", CreateVector(dimensions, 1f, 0f, 0f));
        await Store.UpsertAsync(new[] { chunk }, CancellationToken.None);

        // Act
        await Store.DeleteAsync("document-delete", CancellationToken.None);
        var queryVector = CreateVector(dimensions, 1f, 0f, 0f);
        var results = await ReadAllAsync(Store.QueryAsync(queryVector, top: 1, CancellationToken.None), CancellationToken.None);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public Task Upsert_FailsFast_OnDimensionMismatchAsync()
    {
        // Arrange
        var dimensions = Store.Dimensions;
        var wrongDimensionVector = new float[dimensions + 1];
        var chunk = CreateChunk("chunk-mismatch", "document-mismatch", wrongDimensionVector);

        // Act + Assert
        return AssertDimensionMismatchAsync(() => Store.UpsertAsync(new[] { chunk }, CancellationToken.None));
    }

    [Fact]
    public async Task Query_ReturnsEmpty_WhenStoreIsEmptyAsync()
    {
        // Arrange
        var dimensions = Store.Dimensions;
        var queryVector = CreateVector(dimensions, 1f, 0f, 0f);

        // Act
        var results = await ReadAllAsync(Store.QueryAsync(queryVector, top: 10, CancellationToken.None), CancellationToken.None);

        // Assert
        Assert.Empty(results);
    }
}
