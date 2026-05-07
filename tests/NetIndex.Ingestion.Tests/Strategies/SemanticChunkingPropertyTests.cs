using System.Diagnostics.CodeAnalysis;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Options;
using NetIndex.Ingestion.Strategies;
using NetIndex.Testing.Common;
using Xunit;

namespace NetIndex.Ingestion.Tests.Strategies;

/// <summary>
/// FsCheck property tests for <see cref="SemanticChunkingStrategy"/>.
/// </summary>
[Trait("Category", "PropertyTest")]
[SuppressMessage("Design", "VSTHRD002", Justification = "FsCheck Property methods are synchronous by design")]
public class SemanticChunkingPropertyTests
{
    private static readonly ChunkingConfiguration Config = new ChunkingConfiguration().Semantic();
    private static readonly SemanticChunkingStrategy Strategy = new SemanticChunkingStrategy(
        new FakeEmbeddingGenerator(),
        Microsoft.Extensions.Options.Options.Create(Config));

    /// <summary>
    /// No chunk text should exceed the max character size (default ChunkSize * 4).
    /// </summary>
    [Property]
    public Property ChunkOutput_NoChunkExceeds_MaxSize(NonEmptyString input)
    {
        var result = Strategy.ChunkAsync(input.Get, new ChunkingOptions(512, 64, "\n")).Result;
        return result.All(c => c.Text.Length <= 512 * 4).ToProperty();
    }

    /// <summary>
    /// All chunks should have unique IDs.
    /// </summary>
    [Property]
    public Property ChunkOutput_AllChunksHave_UniqueIds(NonEmptyString input)
    {
        var result = Strategy.ChunkAsync(input.Get, new ChunkingOptions(512, 64, "\n")).Result;
        var ids = result.Select(c => c.Id).ToList();
        return ids.Distinct().Count().Equals(ids.Count).ToProperty();
    }

    /// <summary>
    /// All chunks should carry "pending" as the DocumentId.
    /// </summary>
    [Property]
    public Property ChunkOutput_AllChunksHave_PendingDocumentId(NonEmptyString input)
    {
        var result = Strategy.ChunkAsync(input.Get, new ChunkingOptions(512, 64, "\n")).Result;
        return result.All(c => c.DocumentId == "pending").ToProperty();
    }

    /// <summary>
    /// Chunks should respect sentence boundaries — chunk text should end with a sentence-ending punctuation
    /// when followed by a different chunk (indicating split at sentence boundary, not mid-sentence).
    /// </summary>
    [Property]
    public Property ChunkOutput_SemanticBoundaries_Respected(NonEmptyString input)
    {
        var result = Strategy.ChunkAsync(input.Get, new ChunkingOptions(512, 64, "\n")).Result.ToList();
        if (result.Count <= 1)
        {
            return true.ToProperty();
        }

        // Each chunk should end with a sentence-ending character or the next chunk should
        // start with a capital letter (indicating boundaries were respected at sentence level)
        var boundariesRespected = true;
        for (var i = 0; i < result.Count - 1; i++)
        {
            var currentEnds = result[i].Text;
            var nextStarts = result[i + 1].Text;

            // The current chunk's last character should be punctuation or the next chunk
            // should start with a capital letter
            var endsWithPunctuation = currentEnds.Length == 0 ||
                ".!?".Contains(currentEnds[^1]);

            if (!endsWithPunctuation && nextStarts.Length > 0 && !char.IsUpper(nextStarts[0]))
            {
                boundariesRespected = false;
                break;
            }
        }

        return boundariesRespected.ToProperty();
    }
}