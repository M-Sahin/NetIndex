using NetIndex.Core.Abstractions;
using NetIndex.Storage.Pgvector.Tests.Fixtures;
using NetIndex.Testing.Common;
using Xunit;

namespace NetIndex.Storage.Pgvector.Tests;

/// <summary>
/// Runs the full <see cref="VectorStoreContractSuite"/> against <see cref="PgvectorVectorStore"/>.
/// Each test gets a fresh schema state via <see cref="PostgresFixture.ResetAsync"/>.
/// </summary>
[Collection(TestingConstants.Collections.Pgvector)]
[Trait("Category", "ContractTest")]
public class PgvectorVectorStoreTests : VectorStoreContractSuite
{
    private readonly PostgresFixture _fixture;

    /// <summary>Initializes with the shared <see cref="PostgresFixture"/>.</summary>
    /// <param name="fixture">The fixture providing a fresh store per test.</param>
    public PgvectorVectorStoreTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override IVectorStore Store => _fixture.Store;

    /// <summary>Reset to a clean store state before each test.</summary>
    public override Task InitializeAsync() => _fixture.ResetAsync();

    /// <summary>Cleanup handled by the fixture; nothing to do per-test.</summary>
    public override Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// A failure after the delete, inside the replace transaction (PostgreSQL rejects NUL in text),
    /// rolls back: the old chunk set stays intact and retrievable.
    /// </summary>
    [Fact]
    public async Task Replace_FailureMidTransaction_RollsBackAndKeepsTheOldSetAsync()
    {
        var vector = new float[] { 1f, 0f, 0f, 0f };
        var tenant = new Dictionary<string, string> { [RagChunkMetadata.TenantId] = "tenant-a" };
        await Store.ReplaceDocumentAsync(
            "doc-rollback",
            Enumerable.Range(0, 3).Select(i => new RagChunk($"doc-rollback_chunk_{i}", $"old-{i}", vector, "doc-rollback", tenant)),
            CancellationToken.None);

        var exception = await Record.ExceptionAsync(() => Store.ReplaceDocumentAsync(
            "doc-rollback",
            new[]
            {
                new RagChunk("doc-rollback_chunk_0", "new-0", vector, "doc-rollback", tenant),
                new RagChunk("doc-rollback_chunk_1", "bad\0text", vector, "doc-rollback", tenant),
            },
            CancellationToken.None));

        Assert.IsType<NetIndexStorageException>(exception);
        var texts = new List<string>();
        await foreach (var hit in Store.QueryAsync(vector, top: 10, CancellationToken.None))
        {
            texts.Add(hit.Item.Text);
        }

        Assert.Equal(new[] { "old-0", "old-1", "old-2" }, texts.OrderBy(t => t, StringComparer.Ordinal).ToArray());
    }
}
