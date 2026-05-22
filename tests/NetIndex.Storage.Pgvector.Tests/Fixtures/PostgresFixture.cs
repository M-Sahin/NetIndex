using Microsoft.Extensions.Options;
using NetIndex.Storage.Pgvector;
using NetIndex.Storage.Pgvector.Options;
using NetIndex.Testing.Common;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace NetIndex.Storage.Pgvector.Tests.Fixtures;

/// <summary>
/// xUnit fixture that spins up a Testcontainers PostgreSQL container with pgvector pre-installed
/// and provides a <see cref="PgvectorVectorStore"/> for each test.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime, IResetable
{
    /// <summary>Dimensions used for all test vectors — kept small for fast test execution.</summary>
    public const int TestDimensions = 4;

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    private PgvectorVectorStore? _store;

    /// <summary>Gets the current <see cref="PgvectorVectorStore"/> under test.</summary>
    public PgvectorVectorStore Store => _store!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        _store = CreateStore();
        // Trigger lazy schema initialization so ResetAsync's TRUNCATE has a table to target.
        // DeleteAsync is the only public method that always reaches EnsureInitializedAsync:
        // QueryAsync(top: 0) short-circuits before init, and UpsertAsync(empty) early-returns before init.
        await _store.DeleteAsync("__warmup__", CancellationToken.None).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ResetAsync()
    {
        // TRUNCATE instead of disposing and recreating the store.
        // The store's schema is already initialized; we just clear rows between tests.
        // This avoids the wasted dispose+EnsureInitializedAsync round-trip of recreating the store.
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE TABLE rag_chunks";
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_store is not null)
        {
            await _store.DisposeAsync().ConfigureAwait(false);
        }

        await _container.DisposeAsync().ConfigureAwait(false);
    }

    private PgvectorVectorStore CreateStore()
    {
        var options = new PgvectorOptions
        {
            ConnectionString = _container.GetConnectionString(),
            Dimensions = TestDimensions,
        };
        return new PgvectorVectorStore(new OptionsWrapper<PgvectorOptions>(options));
    }
}
