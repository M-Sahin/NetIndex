using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, RagChunk> _chunks = new(StringComparer.Ordinal);
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

            if (chunk.Embedding.Length != _dimensions)
            {
                throw new NetIndexConfigurationException(
                    $"Embedding dimension mismatch: expected {_dimensions}, got {chunk.Embedding.Length}. " +
                    $"Ensure InMemoryOptions.Dimensions matches IEmbeddingGenerator.Dimensions.",
                    propertyName: "Dimensions",
                    expectedValue: _dimensions,
                    actualValue: chunk.Embedding.Length);
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
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(top, 0, nameof(top));

        if (queryVector.Length != _dimensions)
        {
            throw new NetIndexStorageException(
                $"Query vector dimension mismatch: expected {_dimensions}, got {queryVector.Length}.",
                nameof(InMemoryVectorStore),
                "Query",
                null);
        }

        var matches = _chunks.Values
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

        var keysToRemove = _chunks
            .Where(e => string.Equals(e.Value.DocumentId, documentId, StringComparison.Ordinal))
            .Select(e => e.Key)
            .ToArray();

        foreach (var key in keysToRemove)
        {
            _chunks.TryRemove(key, out _);
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
