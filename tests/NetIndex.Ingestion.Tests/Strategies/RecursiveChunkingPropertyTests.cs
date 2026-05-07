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
/// FsCheck property tests for <see cref="RecursiveChunkingStrategy"/>.
/// </summary>
[Trait("Category", "PropertyTest")]
[SuppressMessage("Design", "VSTHRD002", Justification = "FsCheck Property methods are synchronous by design")]
public class RecursiveChunkingPropertyTests
{
    private static readonly ChunkingConfiguration Config = new ChunkingConfiguration().Recursive();
    private static readonly RecursiveChunkingStrategy Strategy = new RecursiveChunkingStrategy(
        new FakeEmbeddingGenerator(),
        Microsoft.Extensions.Options.Options.Create(Config));

    /// <summary>
    /// No chunk text should exceed the max character size.
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
}