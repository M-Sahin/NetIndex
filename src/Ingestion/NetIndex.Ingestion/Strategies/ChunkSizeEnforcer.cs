using System.Text;
using System.Text.RegularExpressions;
using NetIndex.Core.Abstractions;

namespace NetIndex.Ingestion.Strategies;

/// <summary>
/// Shared final size-enforcing split used by every chunking strategy so that no emitted chunk is
/// longer than the configured <see cref="ChunkingOptions.ChunkSize"/> (4 characters per token).
/// </summary>
/// <remarks>
/// Oversized text is split at sentence boundaries first, then at whitespace, then at hard character
/// boundaries. Pieces are packed greedily; overlap (in characters) is carried from the end of the
/// previous piece into the next and counted against the limit.
/// </remarks>
internal static class ChunkSizeEnforcer
{
    private static readonly Regex SentenceBoundary = new(@"(?<=[.!?]\s)", RegexOptions.Compiled);
    private static readonly Regex WhitespaceBoundary = new(@"(?<=\s)", RegexOptions.Compiled);

    /// <summary>
    /// Returns the chunks with every oversized chunk split, and ids renumbered sequentially.
    /// Chunks already within the limit are passed through unchanged.
    /// </summary>
    public static List<RagChunk> Enforce(
        IEnumerable<RagChunk> chunks,
        int maxChars,
        int overlapChars,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxChars, 0);
        if (overlapChars < 0 || overlapChars >= maxChars)
        {
            overlapChars = 0;
        }

        var result = new List<RagChunk>();
        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (chunk.Text.Length <= maxChars)
            {
                result.Add(chunk);
                continue;
            }

            foreach (var piece in Split(chunk.Text, maxChars, overlapChars))
            {
                result.Add(chunk with { Text = piece });
            }
        }

        for (var i = 0; i < result.Count; i++)
        {
            result[i] = result[i] with { Id = $"chunk_{i}" };
        }

        return result;
    }

    private static List<string> Split(string text, int maxChars, int overlapChars)
    {
        // Leave room for the overlap prefix so prefix + piece never exceeds maxChars.
        var cap = maxChars - overlapChars;

        var atoms = new List<string>();
        Atomize(text, cap, level: 0, atoms);

        var rawPieces = new List<string>();
        var current = new StringBuilder();
        foreach (var atom in atoms)
        {
            if (current.Length > 0 && current.Length + atom.Length > cap)
            {
                rawPieces.Add(current.ToString());
                current.Clear();
            }

            current.Append(atom);
        }

        if (current.Length > 0)
        {
            rawPieces.Add(current.ToString());
        }

        var result = new List<string>();
        for (var i = 0; i < rawPieces.Count; i++)
        {
            var piece = rawPieces[i];
            if (i > 0 && overlapChars > 0)
            {
                piece = Tail(rawPieces[i - 1], overlapChars) + piece;
            }

            piece = piece.Trim();
            if (piece.Length > 0)
            {
                result.Add(piece);
            }
        }

        return result;
    }

    /// <summary>
    /// Breaks <paramref name="text"/> into atoms no longer than <paramref name="cap"/>:
    /// sentences (level 0), then whitespace-delimited words (level 1), then hard character slices (level 2).
    /// </summary>
    private static void Atomize(string text, int cap, int level, List<string> atoms)
    {
        if (text.Length <= cap)
        {
            atoms.Add(text);
            return;
        }

        if (level == 0)
        {
            var parts = SentenceBoundary.Split(text);
            if (parts.Length > 1)
            {
                foreach (var part in parts)
                {
                    Atomize(part, cap, 1, atoms);
                }

                return;
            }

            level = 1;
        }

        if (level == 1)
        {
            var parts = WhitespaceBoundary.Split(text);
            if (parts.Length > 1)
            {
                foreach (var part in parts)
                {
                    Atomize(part, cap, 2, atoms);
                }

                return;
            }
        }

        // Hard character boundaries; never cut between a surrogate pair.
        var start = 0;
        while (start < text.Length)
        {
            var length = Math.Min(cap, text.Length - start);
            if (start + length < text.Length && char.IsHighSurrogate(text[start + length - 1]) && length > 1)
            {
                length--;
            }

            atoms.Add(text.Substring(start, length));
            start += length;
        }
    }

    private static string Tail(string text, int chars)
    {
        var start = Math.Max(0, text.Length - chars);
        if (start > 0 && start < text.Length && char.IsLowSurrogate(text[start]))
        {
            start++;
        }

        return text[start..];
    }
}
