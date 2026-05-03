using NetIndex.Core.Abstractions;

namespace NetIndex.Core;

/// <summary>
/// Default chunking strategy that returns the entire document as a single chunk.
/// </summary>
/// <remarks>
/// Used for zero-config scenarios where no explicit chunking strategy is registered.
/// </remarks>
public sealed class PassThroughChunkingStrategy : IChunkingStrategy
{
    /// <inheritdoc />
    public Task<IEnumerable<RagChunk>> ChunkAsync(
        string text,
        ChunkingOptions? options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(text);

        return Task.FromResult<IEnumerable<RagChunk>>(
        [new RagChunk("chunk_0", text, null, "pass_through", null)]);
    }
}
