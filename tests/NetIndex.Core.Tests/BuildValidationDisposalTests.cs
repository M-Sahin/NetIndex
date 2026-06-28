using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using NSubstitute;
using Xunit;

namespace NetIndex.Core.Tests;

/// <summary>
/// Regression tests for <see cref="INetIndexBuilder.Build"/> disposing its temporary
/// validation provider. A vector store that implements only <see cref="IAsyncDisposable"/>
/// (e.g. <c>SqliteVectorStore</c>) must not cause Build() to throw when the validation
/// provider is torn down — a synchronous <c>Dispose()</c> of such a provider throws
/// "only implements IAsyncDisposable. Use DisposeAsync".
/// </summary>
[Trait("Category", "PipelineContract")]
public sealed class BuildValidationDisposalTests
{
    /// <summary>
    /// Build() must succeed when the configured vector store is registered as a singleton
    /// that implements only IAsyncDisposable. Before the fix this threw
    /// NetIndexConfigurationException wrapping an InvalidOperationException from the
    /// validation provider's synchronous disposal.
    /// </summary>
    [Fact]
    public void Build_WithAsyncDisposableOnlyVectorStore_DoesNotThrow()
    {
        var services = new ServiceCollection();

        var mockEmbedding = Substitute.For<IEmbeddingGenerator>();
        mockEmbedding.Dimensions.Returns(AsyncDisposableOnlyVectorStore.FixedDimensions);

        services.AddSingleton<IEmbeddingGenerator>(mockEmbedding);
        // Register by type so the container *creates* and therefore tracks the store for
        // disposal — exactly how UseSqlite registers SqliteVectorStore. Registering a
        // pre-built instance would not reproduce the bug, since the validation provider
        // only synchronously disposes the disposables it owns.
        services.AddSingleton<IVectorStore, AsyncDisposableOnlyVectorStore>();

        var builder = services.AddNetIndex();

        // Before the fix this threw NetIndexConfigurationException during validation-provider teardown.
        var result = builder.Build();

        Assert.NotNull(result);
    }

    /// <summary>
    /// A vector store that supports only asynchronous disposal, mirroring SqliteVectorStore.
    /// </summary>
    private sealed class AsyncDisposableOnlyVectorStore : IVectorStore, IAsyncDisposable
    {
        public const int FixedDimensions = 384;

        public int Dimensions => FixedDimensions;

        public Task UpsertAsync(IEnumerable<RagChunk> chunks, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async IAsyncEnumerable<SearchResult<RagChunk>> QueryAsync(
            float[] queryVector,
            int top = 5,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task DeleteAsync(string documentId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
