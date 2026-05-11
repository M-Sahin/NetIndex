using NetIndex.Core.Abstractions;
using NetIndex.Storage.Sqlite.Tests.Fixtures;
using NetIndex.Testing.Common;

namespace NetIndex.Storage.Sqlite.Tests;

/// <summary>
/// Runs the full <see cref="VectorStoreContractSuite"/> against <see cref="SqliteVectorStore"/>.
/// Each test gets a fresh in-memory database via <see cref="SqliteFixture.ResetAsync"/>.
/// </summary>
[Collection(TestingConstants.Collections.Sqlite)]
[Trait("Category", "ContractTest")]
public class SqliteVectorStoreTests : VectorStoreContractSuite
{
    private readonly SqliteFixture _fixture;

    /// <summary>Initializes with the shared <see cref="SqliteFixture"/>.</summary>
    /// <param name="fixture">The fixture providing a fresh store per test.</param>
    public SqliteVectorStoreTests(SqliteFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override IVectorStore Store => _fixture.Store;

    /// <summary>
    /// <see cref="SqliteVectorStore"/> throws <see cref="NetIndexConfigurationException"/> on dimension mismatch,
    /// matching the <see cref="VectorStoreContractSuite"/> default.
    /// </summary>
    protected override Type DimensionMismatchExceptionType => typeof(NetIndexConfigurationException);

    /// <summary>Reset to a clean in-memory database before each test.</summary>
    public override Task InitializeAsync() => _fixture.ResetAsync();

    /// <summary>Cleanup handled by the fixture; nothing to do per-test.</summary>
    public override Task DisposeAsync() => Task.CompletedTask;
}
