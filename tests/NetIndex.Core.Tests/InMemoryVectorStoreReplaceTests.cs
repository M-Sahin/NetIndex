#pragma warning disable CS1591
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using Xunit;

namespace NetIndex.Core.Tests;

public sealed class InMemoryVectorStoreReplaceTests
{
    private static RagChunk Chunk(string id, string doc, string? tenant)
    {
        var embedding = new float[384];
        embedding[0] = 1f;
        return new RagChunk(
            id,
            $"text-{id}",
            embedding,
            doc,
            tenant is null ? null : new Dictionary<string, string> { [RagChunkMetadata.TenantId] = tenant });
    }

    private static async Task<List<string>> IdsAsync(IVectorStore store)
    {
        var query = new float[384];
        query[0] = 1f;
        var ids = new List<string>();
        await foreach (var hit in store.QueryAsync(query, top: 100))
        {
            ids.Add(hit.Item.Id);
        }

        return ids.OrderBy(i => i, StringComparer.Ordinal).ToList();
    }

    [Fact]
    public async Task Replace_ShorterSet_RemovesStaleChunksAsync()
    {
        IVectorStore store = new InMemoryVectorStore();
        await store.ReplaceDocumentAsync("d", Enumerable.Range(0, 5).Select(i => Chunk($"d_chunk_{i}", "d", "a")));

        await store.ReplaceDocumentAsync("d", Enumerable.Range(0, 2).Select(i => Chunk($"d_chunk_{i}", "d", "a")));

        Assert.Equal(new[] { "d_chunk_0", "d_chunk_1" }, await IdsAsync(store));
    }

    [Fact]
    public async Task Replace_CrossTenant_IsRejectedAndNothingChangesAsync()
    {
        IVectorStore store = new InMemoryVectorStore();
        await store.ReplaceDocumentAsync("d", Enumerable.Range(0, 3).Select(i => Chunk($"d_chunk_{i}", "d", "a")));

        await Assert.ThrowsAsync<NetIndexAuthorizationException>(
            () => store.ReplaceDocumentAsync("d", new[] { Chunk("d_chunk_0", "d", "b") }));

        Assert.Equal(3, (await IdsAsync(store)).Count);
    }

    [Fact]
    public async Task Replace_UnderConcurrentQueries_NeverShowsAMixedOrEmptySetAsync()
    {
        IVectorStore store = new InMemoryVectorStore();
        await store.ReplaceDocumentAsync("d", Enumerable.Range(0, 5).Select(i => Chunk($"d_chunk_{i}", "d", "a")));

        using var cts = new CancellationTokenSource();
        var violations = 0;
        var reader = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                var count = (await IdsAsync(store)).Count;
                if (count != 5 && count != 2)
                {
                    Interlocked.Increment(ref violations);
                }
            }
        });

        for (var round = 0; round < 300; round++)
        {
            var size = round % 2 == 0 ? 2 : 5;
            await store.ReplaceDocumentAsync("d", Enumerable.Range(0, size).Select(i => Chunk($"d_chunk_{i}", "d", "a")));
        }

        await cts.CancelAsync();
        await reader;
        Assert.Equal(0, violations);
    }
}
