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

    private static RagChunk CreateTenantChunk(string chunkId, string documentId, float[] embedding, string? tenant)
        => new(
            chunkId,
            $"text-{chunkId}",
            embedding,
            documentId,
            tenant is null ? null : new Dictionary<string, string> { [RagChunkMetadata.TenantId] = tenant });

    private async Task<string[]> QueryIdsAsync()
    {
        var results = await ReadAllAsync(
            Store.QueryAsync(CreateVector(Store.Dimensions, 1f, 0f, 0f), top: 50, CancellationToken.None),
            CancellationToken.None);
        return results.Select(r => r.Item.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
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

    [Fact]
    public async Task Replace_ShorterSet_LeavesOnlyTheNewChunksAsync()
    {
        // Arrange: 5 chunks, then re-ingest as 2
        var vector = CreateVector(Store.Dimensions, 1f, 0f, 0f);
        await Store.ReplaceDocumentAsync(
            "doc-replace",
            Enumerable.Range(0, 5).Select(i => CreateTenantChunk($"doc-replace_chunk_{i}", "doc-replace", vector, "tenant-a")),
            CancellationToken.None);
        Assert.Equal(5, (await QueryIdsAsync()).Length);

        // Act
        await Store.ReplaceDocumentAsync(
            "doc-replace",
            Enumerable.Range(0, 2).Select(i => CreateTenantChunk($"doc-replace_chunk_{i}", "doc-replace", vector, "tenant-a")),
            CancellationToken.None);

        // Assert
        Assert.Equal(new[] { "doc-replace_chunk_0", "doc-replace_chunk_1" }, await QueryIdsAsync());
    }

    [Fact]
    public async Task Replace_SameAndLongerSet_LeavesExactlyTheNewSetAsync()
    {
        var vector = CreateVector(Store.Dimensions, 1f, 0f, 0f);
        var other = CreateTenantChunk("other_chunk_0", "other", vector, "tenant-a");
        await Store.UpsertAsync(new[] { other }, CancellationToken.None);

        // 2 -> 2 with changed text
        await Store.ReplaceDocumentAsync(
            "doc-same",
            Enumerable.Range(0, 2).Select(i => CreateTenantChunk($"doc-same_chunk_{i}", "doc-same", vector, "tenant-a")),
            CancellationToken.None);
        var rewritten = Enumerable.Range(0, 2)
            .Select(i => new RagChunk($"doc-same_chunk_{i}", $"v2-{i}", vector, "doc-same", new Dictionary<string, string> { [RagChunkMetadata.TenantId] = "tenant-a" }))
            .ToArray();
        await Store.ReplaceDocumentAsync("doc-same", rewritten, CancellationToken.None);

        var results = await ReadAllAsync(Store.QueryAsync(vector, top: 50, CancellationToken.None), CancellationToken.None);
        Assert.Equal(
            new[] { "v2-0", "v2-1" },
            results.Where(r => r.Item.DocumentId == "doc-same").Select(r => r.Item.Text).OrderBy(t => t, StringComparer.Ordinal).ToArray());

        // 2 -> 5
        await Store.ReplaceDocumentAsync(
            "doc-same",
            Enumerable.Range(0, 5).Select(i => CreateTenantChunk($"doc-same_chunk_{i}", "doc-same", vector, "tenant-a")),
            CancellationToken.None);

        var ids = await QueryIdsAsync();
        Assert.Equal(6, ids.Length);
        Assert.Contains("other_chunk_0", ids);
        Assert.Equal(5, ids.Count(id => id.StartsWith("doc-same_", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Replace_CrossTenantCollision_IsRejectedAndLeavesTheOriginalIntactAsync()
    {
        var vector = CreateVector(Store.Dimensions, 1f, 0f, 0f);
        await Store.ReplaceDocumentAsync(
            "doc-1",
            Enumerable.Range(0, 3).Select(i => CreateTenantChunk($"doc-1_chunk_{i}", "doc-1", vector, "tenant-a")),
            CancellationToken.None);

        // Tenant B tries to take over doc-1 with a different-size set.
        var exception = await Record.ExceptionAsync(() => Store.ReplaceDocumentAsync(
            "doc-1",
            new[] { CreateTenantChunk("doc-1_chunk_0", "doc-1", vector, "tenant-b") },
            CancellationToken.None));

        var auth = Assert.IsType<NetIndexAuthorizationException>(exception);
        Assert.Equal(IVectorStore.CROSS_TENANT_DOCUMENT_COLLISION, auth.FailureReason);

        var results = await ReadAllAsync(Store.QueryAsync(vector, top: 50, CancellationToken.None), CancellationToken.None);
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal("tenant-a", r.Item.Metadata![RagChunkMetadata.TenantId]));
    }

    [Fact]
    public async Task Replace_ExistingChunksWithoutTenant_AreRejectedForATenantedReplaceAsync()
    {
        var vector = CreateVector(Store.Dimensions, 1f, 0f, 0f);
        await Store.UpsertAsync(new[] { CreateTenantChunk("doc-2_chunk_0", "doc-2", vector, null) }, CancellationToken.None);

        var exception = await Record.ExceptionAsync(() => Store.ReplaceDocumentAsync(
            "doc-2",
            new[] { CreateTenantChunk("doc-2_chunk_0", "doc-2", vector, "tenant-b") },
            CancellationToken.None));

        Assert.IsType<NetIndexAuthorizationException>(exception);
        Assert.Equal(new[] { "doc-2_chunk_0" }, await QueryIdsAsync());
    }

    [Fact]
    public async Task Replace_WithInvalidNewChunk_FailsAndLeavesTheOldSetIntactAsync()
    {
        var vector = CreateVector(Store.Dimensions, 1f, 0f, 0f);
        await Store.ReplaceDocumentAsync(
            "doc-3",
            Enumerable.Range(0, 3).Select(i => CreateTenantChunk($"doc-3_chunk_{i}", "doc-3", vector, "tenant-a")),
            CancellationToken.None);

        var broken = new[]
        {
            CreateTenantChunk("doc-3_chunk_0", "doc-3", vector, "tenant-a"),
            CreateTenantChunk("doc-3_chunk_1", "doc-3", new float[Store.Dimensions + 1], "tenant-a"),
        };
        var exception = await Record.ExceptionAsync(() => Store.ReplaceDocumentAsync("doc-3", broken, CancellationToken.None));

        Assert.NotNull(exception);
        Assert.Equal(3, (await QueryIdsAsync()).Length);
    }

    [Fact]
    public async Task Replace_ChunkOfAnotherDocument_IsRejectedAsync()
    {
        var vector = CreateVector(Store.Dimensions, 1f, 0f, 0f);
        var exception = await Record.ExceptionAsync(() => Store.ReplaceDocumentAsync(
            "doc-4",
            new[] { CreateTenantChunk("x_chunk_0", "not-doc-4", vector, "tenant-a") },
            CancellationToken.None));

        Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.Empty(await QueryIdsAsync());
    }

    [Fact]
    public async Task Replace_UnderConcurrentQueries_NeverShowsAMixedOrEmptySetAsync()
    {
        var vector = CreateVector(Store.Dimensions, 1f, 0f, 0f);
        RagChunk[] Set(int n) => Enumerable.Range(0, n)
            .Select(i => new RagChunk(
                $"doc-c_chunk_{i}",
                $"t{n}-{i}",
                vector,
                "doc-c",
                new Dictionary<string, string> { [RagChunkMetadata.TenantId] = "tenant-a" }))
            .ToArray();
        await Store.ReplaceDocumentAsync("doc-c", Set(5), CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var violations = 0;
        var reader = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                var results = await ReadAllAsync(Store.QueryAsync(vector, top: 20, CancellationToken.None), CancellationToken.None);
                var texts = results.Select(r => r.Item.Text).ToList();
                var consistent = (texts.Count == 5 && texts.All(t => t.StartsWith("t5-", StringComparison.Ordinal)))
                    || (texts.Count == 2 && texts.All(t => t.StartsWith("t2-", StringComparison.Ordinal)));
                if (!consistent)
                {
                    Interlocked.Increment(ref violations);
                }
            }
        });

        for (var round = 0; round < 40; round++)
        {
            await Store.ReplaceDocumentAsync("doc-c", Set(round % 2 == 0 ? 2 : 5), CancellationToken.None);
        }

        await cts.CancelAsync();
        await reader;
        Assert.Equal(0, violations);
    }

    [Fact]
    public async Task Replace_NewChunkIdOwnedByAnotherDocument_IsRejectedAndNothingChangesAsync()
    {
        var vector = CreateVector(Store.Dimensions, 1f, 0f, 0f);
        await Store.ReplaceDocumentAsync(
            "doc-x",
            new[] { CreateTenantChunk("doc-x_chunk_0", "doc-x", vector, "tenant-a") },
            CancellationToken.None);

        // Another document (and tenant) tries to take the row over through a colliding chunk id.
        var exception = await Record.ExceptionAsync(() => Store.ReplaceDocumentAsync(
            "doc-y",
            new[] { CreateTenantChunk("doc-x_chunk_0", "doc-y", vector, "tenant-b") },
            CancellationToken.None));

        var auth = Assert.IsType<NetIndexAuthorizationException>(exception);
        Assert.Equal(IVectorStore.CROSS_TENANT_DOCUMENT_COLLISION, auth.FailureReason);
        var results = await ReadAllAsync(Store.QueryAsync(vector, top: 50, CancellationToken.None), CancellationToken.None);
        var only = Assert.Single(results);
        Assert.Equal("doc-x", only.Item.DocumentId);
        Assert.Equal("tenant-a", only.Item.Metadata![RagChunkMetadata.TenantId]);
    }

    [Fact]
    public async Task Replace_DuplicateChunkIdsInTheNewSet_AreRejectedAsync()
    {
        var vector = CreateVector(Store.Dimensions, 1f, 0f, 0f);
        var exception = await Record.ExceptionAsync(() => Store.ReplaceDocumentAsync(
            "doc-d",
            new[]
            {
                CreateTenantChunk("doc-d_chunk_0", "doc-d", vector, "tenant-a"),
                CreateTenantChunk("doc-d_chunk_0", "doc-d", vector, "tenant-a"),
            },
            CancellationToken.None));

        Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.Empty(await QueryIdsAsync());
    }

    [Fact]
    public async Task Replace_MixedTenantNewSet_IsRejectedAsMalformedAsync()
    {
        var vector = CreateVector(Store.Dimensions, 1f, 0f, 0f);
        var exception = await Record.ExceptionAsync(() => Store.ReplaceDocumentAsync(
            "doc-m",
            new[]
            {
                CreateTenantChunk("doc-m_chunk_0", "doc-m", vector, "tenant-a"),
                CreateTenantChunk("doc-m_chunk_1", "doc-m", vector, "tenant-b"),
            },
            CancellationToken.None));

        Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.Empty(await QueryIdsAsync());
    }

    [Fact]
    public async Task Replace_EmptyOrTenantlessSetOverTenantStampedChunks_IsRejectedAndLeavesTheOriginalIntactAsync()
    {
        var vector = CreateVector(Store.Dimensions, 1f, 0f, 0f);
        await Store.ReplaceDocumentAsync(
            "doc-e",
            Enumerable.Range(0, 2).Select(i => CreateTenantChunk($"doc-e_chunk_{i}", "doc-e", vector, "tenant-a")),
            CancellationToken.None);

        var empty = await Record.ExceptionAsync(() => Store.ReplaceDocumentAsync("doc-e", Array.Empty<RagChunk>(), CancellationToken.None));
        var tenantless = await Record.ExceptionAsync(() => Store.ReplaceDocumentAsync(
            "doc-e",
            new[] { CreateTenantChunk("doc-e_chunk_0", "doc-e", vector, null) },
            CancellationToken.None));

        Assert.IsType<NetIndexAuthorizationException>(empty);
        Assert.IsType<NetIndexAuthorizationException>(tenantless);
        var results = await ReadAllAsync(Store.QueryAsync(vector, top: 50, CancellationToken.None), CancellationToken.None);
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("tenant-a", r.Item.Metadata![RagChunkMetadata.TenantId]));
    }
}
