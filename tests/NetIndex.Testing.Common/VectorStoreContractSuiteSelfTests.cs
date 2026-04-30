using NetIndex.Core.Abstractions;

namespace NetIndex.Testing.Common;

public sealed class VectorStoreContractSuiteSelfTests : VectorStoreContractSuite
{
    private readonly ContractTestVectorStore _store = new(3);

    protected override IVectorStore Store => _store;

    public override Task InitializeAsync()
    {
        _store.Reset();
        return Task.CompletedTask;
    }

    private sealed class ContractTestVectorStore : IVectorStore
    {
        private readonly List<RagChunk> _chunks = new();

        public ContractTestVectorStore(int dimensions)
        {
            Dimensions = dimensions;
        }

        public int Dimensions { get; }

        public Task UpsertAsync(IEnumerable<RagChunk> chunks, CancellationToken cancellationToken = default)
        {
            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (chunk.Embedding is null || chunk.Embedding.Length != Dimensions)
                {
                    throw new NetIndexConfigurationException(
                        "Embedding dimension mismatch.",
                        nameof(RagChunk.Embedding),
                        Dimensions,
                        chunk.Embedding?.Length);
                }

                _chunks.RemoveAll(existing => existing.Id == chunk.Id);
                _chunks.Add(chunk);
            }

            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<SearchResult<RagChunk>> QueryAsync(
            float[] queryVector,
            int top = 5,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var orderedResults = _chunks
                .Select(chunk => new SearchResult<RagChunk>(chunk, CosineSimilarity(queryVector, chunk.Embedding!), chunk.DocumentId))
                .OrderByDescending(result => result.Score)
                .Take(top)
                .ToArray();

            foreach (var result in orderedResults)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return result;
                await Task.Yield();
            }
        }

        public Task DeleteAsync(string documentId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _chunks.RemoveAll(chunk => string.Equals(chunk.DocumentId, documentId, StringComparison.Ordinal));
            return Task.CompletedTask;
        }

        public void Reset() => _chunks.Clear();

        private static float CosineSimilarity(float[] left, float[] right)
        {
            double dotProduct = 0;
            double leftMagnitude = 0;
            double rightMagnitude = 0;

            for (var index = 0; index < left.Length; index++)
            {
                dotProduct += left[index] * right[index];
                leftMagnitude += left[index] * left[index];
                rightMagnitude += right[index] * right[index];
            }

            if (leftMagnitude == 0 || rightMagnitude == 0)
            {
                return 0f;
            }

            return (float)(dotProduct / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude)));
        }
    }
}