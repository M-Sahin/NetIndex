using System.Runtime.CompilerServices;
using System.Text;
using NetIndex.Core.Abstractions;

namespace NetIndex.Evaluation.Tests.TestSupport;

/// <summary>
/// Test-only <see cref="IChatClient"/> that generates answers deterministically from the supplied
/// query and context tokens — never from query IDs or expected scores.
/// </summary>
/// <remarks>
/// <para>
/// The answer consists of unique content tokens extracted from the context chunks (bounded to 15),
/// ordered by query overlap first and context order second. This keeps the emitted text grounded in
/// the retrieved context while still making the result a function of both prompt and context.
/// </para>
/// <para>
/// Capture: after each <see cref="GenerateStreamingAsync"/> call the received context is stored by
/// prompt so the evaluation runner can score faithfulness without re-querying the pipeline or
/// reflecting into internal state.
/// </para>
/// </remarks>
internal sealed class DeterministicFaithfulnessChatClient : IChatClient
{
    private const int MaxContextTokens = 15;

    private readonly Dictionary<string, IReadOnlyList<RagChunk>> _capturedChunksByPrompt =
        new(StringComparer.Ordinal);
    private readonly object _captureLock = new();

    // Closed-class stopwords: same list as FaithfulnessMetrics and DeterministicTokenEmbeddingGenerator
    // so that every token placed in the answer is also recognized as a content token by the metric.
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "is", "are", "was", "were", "be", "been", "being", "to", "of", "in", "on", "for",
        "and", "or", "but", "with", "by", "from", "this", "that", "these", "those", "it", "its", "as", "at",
        "can", "what", "while", "i", "you", "he", "she", "they", "we", "do", "does", "did", "have", "has",
        "had", "not", "no", "so", "if", "then", "than", "which", "who", "whom", "when", "where", "why",
        "how", "s", "t", "re", "ve", "ll", "d", "m", "there", "here", "all", "any", "each", "such", "into",
        "before", "after", "up", "down", "out", "about", "between", "through", "during",
    };

    /// <summary>
    /// Clears any previously captured context for <paramref name="prompt"/> before a new evaluation call.
    /// </summary>
    public void ResetCapture(string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        lock (_captureLock)
        {
            _capturedChunksByPrompt.Remove(prompt);
        }
    }

    /// <summary>
    /// Returns the context chunks captured for <paramref name="prompt"/> during a prior generation call.
    /// Throws if no context was captured, which prevents stale last-call data from being scored.
    /// </summary>
    public IReadOnlyList<RagChunk> GetCapturedChunks(string prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        lock (_captureLock)
        {
            if (_capturedChunksByPrompt.TryGetValue(prompt, out var chunks))
            {
                return chunks;
            }
        }

        throw new InvalidOperationException($"No context chunks were captured for prompt '{prompt}'.");
    }

    /// <inheritdoc />
    public Task<string> GenerateAsync(
        string prompt,
        IEnumerable<RagChunk> context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var chunks = context.ToList().AsReadOnly();
        Capture(prompt, chunks);

        return Task.FromResult(BuildAnswer(prompt, chunks));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<GenerationChunk> GenerateStreamingAsync(
        string prompt,
        IEnumerable<RagChunk> context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var chunks = context.ToList().AsReadOnly();
        Capture(prompt, chunks);

        return StreamAnswerAsync(BuildAnswer(prompt, chunks), cancellationToken);
    }

    private void Capture(string prompt, IReadOnlyList<RagChunk> chunks)
    {
        lock (_captureLock)
        {
            _capturedChunksByPrompt[prompt] = chunks;
        }
    }

    private static string BuildAnswer(string prompt, IReadOnlyList<RagChunk> chunks)
    {
        if (chunks.Count == 0)
        {
            return string.Empty;
        }

        var promptTokens = ContentTokens(prompt).ToHashSet(StringComparer.Ordinal);
        var tokens = new List<string>(MaxContextTokens);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        AddContextTokens(chunks, token => promptTokens.Contains(token), seen, tokens);
        AddContextTokens(chunks, token => !promptTokens.Contains(token), seen, tokens);

        return tokens.Count > 0 ? string.Join(" ", tokens) : string.Empty;
    }

    private static void AddContextTokens(
        IReadOnlyList<RagChunk> chunks,
        Func<string, bool> predicate,
        HashSet<string> seen,
        List<string> tokens)
    {
        foreach (var chunk in chunks)
        {
            foreach (var token in ContentTokens(chunk.Text))
            {
                if (predicate(token) && seen.Add(token))
                {
                    tokens.Add(token);
                    if (tokens.Count >= MaxContextTokens)
                    {
                        return;
                    }
                }
            }
        }
    }

    private static async IAsyncEnumerable<GenerationChunk> StreamAnswerAsync(
        string answer,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var words = answer.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new GenerationChunk(word + " ", IsComplete: false, FinishReason: FinishReason.Stop);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Terminal chunk: IsComplete = true, empty text — always emitted so disposal/logging runs.
        yield return new GenerationChunk(string.Empty, IsComplete: true, FinishReason: FinishReason.Stop);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static IEnumerable<string> ContentTokens(string text)
    {
        var builder = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (builder.Length > 0)
            {
                var token = builder.ToString();
                builder.Clear();
                if (!StopWords.Contains(token))
                {
                    yield return token;
                }
            }
        }

        if (builder.Length > 0)
        {
            var last = builder.ToString();
            if (!StopWords.Contains(last))
            {
                yield return last;
            }
        }
    }
}
