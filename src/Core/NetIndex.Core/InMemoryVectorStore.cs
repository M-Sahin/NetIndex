using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using NetIndex.Core.Abstractions;

namespace NetIndex.Core;

/// <summary>
/// In-memory vector store default used by zero-config setup.
/// </summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, RagChunk> _chunks = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public int Dimensions { get; } = 384;

    /// <inheritdoc />
    public Task UpsertAsync(IEnumerable<RagChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

            _chunks[chunk.Id] = chunk;
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

        var matches = _chunks.Values
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

        var candidateIds = _chunks
            .Where(entry => string.Equals(entry.Value.DocumentId, documentId, StringComparison.Ordinal))
            .Select(entry => entry.Key)
            .ToArray();

        foreach (var id in candidateIds)
        {
            _chunks.TryRemove(id, out _);
        }

        return Task.CompletedTask;
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
