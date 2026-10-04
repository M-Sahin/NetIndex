using System.Runtime.CompilerServices;
using NetIndex.Core.Abstractions;

namespace NetIndex.Core;

/// <summary>
/// In-memory vector store default used by zero-config setup.
/// </summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    // All reads and writes go through _gate so that ReplaceDocumentAsync is atomic for queries.
    private readonly object _gate = new();
    private readonly Dictionary<string, RagChunk> _chunks = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public int Dimensions { get; } = 384;

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
        if (queryVector.Length != Dimensions)
        {
            throw new NetIndexStorageException(
                $"Query vector dimension mismatch: expected {Dimensions}, got {queryVector.Length}.",
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
            .Select(chunk => new SearchResult<RagChunk>(chunk, CosineSimilarity(queryVector, chunk.Embedding!), chunk.Id))
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

            foreach (var chunk in chunkList)
            {
                if (_chunks.TryGetValue(chunk.Id, out var owner))
                {
                    string? ownerTenant = null;
                    owner.Metadata?.TryGetValue(RagChunkMetadata.TenantId, out ownerTenant);
                    if (!string.Equals(owner.DocumentId, documentId, StringComparison.Ordinal)
                        || !string.Equals(ownerTenant, newTenant, StringComparison.Ordinal))
                    {
                        throw IVectorStore.CrossTenantCollision(documentId, newTenant);
                    }
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
        var ids = _chunks
            .Where(entry => string.Equals(entry.Value.DocumentId, documentId, StringComparison.Ordinal))
            .Select(entry => entry.Key)
            .ToArray();

        foreach (var id in ids)
        {
            _chunks.Remove(id);
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

        if (chunk.Embedding.Length != Dimensions)
        {
            throw new NetIndexStorageException(
                $"Embedding dimension mismatch: expected {Dimensions}, got {chunk.Embedding.Length}.",
                nameof(InMemoryVectorStore),
                "Upsert",
                chunk.DocumentId);
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
