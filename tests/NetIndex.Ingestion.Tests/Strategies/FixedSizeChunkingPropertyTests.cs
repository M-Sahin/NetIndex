using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Options;
using NetIndex.Ingestion.Strategies;
using Xunit;

namespace NetIndex.Ingestion.Tests.Strategies;

/// <summary>
/// FsCheck property tests for <see cref="FixedSizeChunkingStrategy"/>.
/// </summary>
[Trait("Category", "PropertyTest")]
[SuppressMessage("Design", "VSTHRD002", Justification = "FsCheck Property methods are synchronous by design")]
public class FixedSizeChunkingPropertyTests
{
    private static readonly ChunkingConfiguration Config = new ChunkingConfiguration().FixedSize(100, 10);
    private static readonly FixedSizeChunkingStrategy Strategy = new FixedSizeChunkingStrategy(Microsoft.Extensions.Options.Options.Create(Config));

    /// <summary>
    /// No chunk text should exceed the max character size (ChunkSize * 4).
    /// </summary>
    [Property]
    public Property ChunkOutput_NoChunkExceeds_MaxSize(NonEmptyString input)
    {
        var result = Strategy.ChunkAsync(input.Get, new ChunkingOptions(100, 10, "\n")).Result;
        return result.All(c => c.Text.Length <= 400).ToProperty();
    }

    /// <summary>
    /// All chunks should have unique IDs.
    /// </summary>
    [Property]
    public Property ChunkOutput_AllChunksHave_UniqueIds(NonEmptyString input)
    {
        var result = Strategy.ChunkAsync(input.Get, new ChunkingOptions(100, 10, "\n")).Result;
        var ids = result.Select(c => c.Id).ToList();
        return ids.Distinct().Count().Equals(ids.Count).ToProperty();
    }

    /// <summary>
    /// All chunks should carry "pending" as the DocumentId (set by pipeline later).
    /// </summary>
    [Property]
    public Property ChunkOutput_AllChunksHave_PendingDocumentId(NonEmptyString input)
    {
        var result = Strategy.ChunkAsync(input.Get, new ChunkingOptions(100, 10, "\n")).Result;
        return result.All(c => c.DocumentId == "pending").ToProperty();
    }

    /// <summary>
    /// The sum of all chunk text lengths should approximately equal the original text length (allowing for overlap).
    /// </summary>
    [Property]
    public Property ChunkOutput_TotalLength_EqualsInputLength(NonEmptyString input)
    {
        var options = new ChunkingOptions(100, 10, "\n");
        var result = Strategy.ChunkAsync(input.Get, options).Result.ToList();
        if (result.Count <= 1)
        {
            return true.ToProperty();
        }

        // Total chunk text = input + overlap content duplicated across boundaries
        var totalChunkText = result.Sum(c => (long)c.Text.Length);
        var overlapChars = 10 * 4;
        var maxOverlapTotal = (long)(result.Count - 1) * overlapChars;
        var inputLength = (long)input.Get.Length;

        // Total should be >= input (overlap adds content) and <= input + max possible overlap
        return (totalChunkText >= inputLength && totalChunkText <= inputLength + maxOverlapTotal).ToProperty();
    }

    /// <summary>
    /// The overlap between consecutive chunks should be approximately correct.
    /// </summary>
    [Property]
    public Property ChunkOutput_Overlap_IsApproximatelyCorrect(NonEmptyString input)
    {
        var options = new ChunkingOptions(100, 10, "\n");
        var result = Strategy.ChunkAsync(input.Get, options).Result.ToList();
        if (result.Count < 2)
        {
            return true.ToProperty();
        }

        var overlapChars = 10 * 4;
        var allOverlapOk = true;
        for (var i = 1; i < result.Count; i++)
        {
            var previousEnd = result[i - 1].Text.Length >= overlapChars
                ? result[i - 1].Text[^Math.Min(overlapChars, result[i - 1].Text.Length)..]
                : result[i - 1].Text;

            if (!result[i].Text.StartsWith(previousEnd[..Math.Min(previousEnd.Length, result[i].Text.Length)]))
            {
                allOverlapOk = false;
                break;
            }
        }

        return allOverlapOk.ToProperty();
    }
}