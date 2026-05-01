using NetIndex.Core.Abstractions;

namespace NetIndex.Core;

/// <summary>
/// In-memory chat client default used by zero-config setup.
/// </summary>
public sealed class InMemoryChatClient : IChatClient
{
    /// <inheritdoc />
    public Task<string> GenerateAsync(string prompt, IEnumerable<RagChunk> context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(context);

        return Task.FromResult("NetIndex is running with in-memory defaults.");
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<GenerationChunk> GenerateStreamingAsync(
        string prompt,
        IEnumerable<RagChunk> context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(context);

        yield return new GenerationChunk("NetIndex is running with in-memory defaults.", false, FinishReason.Stop);
        await Task.Yield();
        yield return new GenerationChunk(string.Empty, true, FinishReason.Stop);
    }
}
