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
}
