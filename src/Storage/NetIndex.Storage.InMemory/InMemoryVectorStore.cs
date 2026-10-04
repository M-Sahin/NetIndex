using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Storage.InMemory.Options;

namespace NetIndex.Storage.InMemory;

/// <summary>
/// Thread-safe in-memory vector store for local development and testing.
/// </summary>
/// <remarks>
/// Data is lost on application restart — not suitable for production persistence.
/// </remarks>
public sealed class InMemoryVectorStore : IVectorStore
{
    // All reads and writes go through _gate so that ReplaceDocumentAsync is atomic for queries.
    private readonly object _gate = new();
    private readonly Dictionary<string, RagChunk> _chunks = new(StringComparer.Ordinal);
    private readonly int _dimensions;

    /// <summary>Initializes with the configured in-memory options.</summary>
    public InMemoryVectorStore(IOptions<InMemoryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opt = options.Value;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(opt.Dimensions, 0, nameof(opt.Dimensions));
        _dimensions = opt.Dimensions;
    }

    /// <inheritdoc />
    public int Dimensions => _dimensions;

    /// <inheritdoc />
    public Task UpsertAsync(IEnumerable<RagChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        var chunkList = chunks.ToList();
        foreach (var chunk in chunkList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateChunk(chunk);
        }

        lock (_gate)
        {
            foreach (var chunk in chunkList)
            {
                _chunks[chunk.Id] = chunk;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SearchResult<RagChunk>> QueryAsync(
        float[] queryVector,
        int top = 5,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryVector);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(top, 0, nameof(top));

        if (queryVector.Length != _dimensions)
        {
            throw new NetIndexStorageException(
                $"Query vector dimension mismatch: expected {_dimensions}, got {queryVector.Length}.",
                nameof(InMemoryVectorStore),
                "Query",
                null);
        }

        RagChunk[] snapshot;
        lock (_gate)
        {
            snapshot = _chunks.Values.ToArray();
        }

        var matches = snapshot
            .Where(chunk => chunk.Embedding is not null)
            .Select(chunk => new SearchResult<RagChunk>(chunk, CosineSimilarity(queryVector, chunk.Embedding!), chunk.DocumentId))
            .OrderByDescending(result => result.Score)
            .Take(top)
            .ToArray();

        foreach (var match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return match;
            await Task.Yield();
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            RemoveDocument(documentId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ReplaceDocumentAsync(
        string documentId,
        IEnumerable<RagChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        var chunkList = chunks.ToList();
        var newTenant = IVectorStore.ValidateReplacement(documentId, chunkList);
        foreach (var chunk in chunkList)
        {
            ValidateChunk(chunk);
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            foreach (var existing in _chunks.Values)
            {
                if (!string.Equals(existing.DocumentId, documentId, StringComparison.Ordinal))
                {
                    continue;
                }

                string? existingTenant = null;
                existing.Metadata?.TryGetValue(RagChunkMetadata.TenantId, out existingTenant);
                if (!string.Equals(existingTenant, newTenant, StringComparison.Ordinal))
                {
                    throw IVectorStore.CrossTenantCollision(documentId, newTenant);
                }
            }

            RemoveDocument(documentId);
            foreach (var chunk in chunkList)
            {
                _chunks[chunk.Id] = chunk;
            }
        }

        return Task.CompletedTask;
    }

    private void RemoveDocument(string documentId)
    {
        var keysToRemove = _chunks
            .Where(e => string.Equals(e.Value.DocumentId, documentId, StringComparison.Ordinal))
            .Select(e => e.Key)
            .ToArray();

        foreach (var key in keysToRemove)
        {
            _chunks.Remove(key);
        }
    }

    private void ValidateChunk(RagChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        if (chunk.Embedding is null)
        {
            throw new NetIndexStorageException(
                "Chunk embedding is required for upsert.",
                nameof(InMemoryVectorStore),
                "Upsert",
                chunk.DocumentId);
        }

        if (chunk.Embedding.Length != _dimensions)
        {
            throw new NetIndexConfigurationException(
                $"Embedding dimension mismatch: expected {_dimensions}, got {chunk.Embedding.Length}. " +
                $"Ensure InMemoryOptions.Dimensions matches IEmbeddingGenerator.Dimensions.",
                propertyName: "Dimensions",
                expectedValue: _dimensions,
                actualValue: chunk.Embedding.Length);
        }
    }

    private static float CosineSimilarity(float[] left, float[] right)
    {
        var dot = 0f;
        var leftMagnitude = 0f;
        var rightMagnitude = 0f;

        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }

        if (leftMagnitude == 0 || rightMagnitude == 0)
        {
            return 0;
        }

        return dot / (float)(Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }
}
