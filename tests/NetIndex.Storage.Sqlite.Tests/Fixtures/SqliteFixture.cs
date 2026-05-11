using Microsoft.Extensions.Options;
using NetIndex.Storage.Sqlite;
using NetIndex.Storage.Sqlite.Options;
using NetIndex.Testing.Common;

namespace NetIndex.Storage.Sqlite.Tests.Fixtures;

/// <summary>
/// xUnit fixture providing a fresh in-memory <see cref="SqliteVectorStore"/> for each test.
/// Uses <c>Data Source=:memory:</c> — no Docker or external process required.
/// </summary>
public sealed class SqliteFixture : IAsyncLifetime, IResetable
{
    /// <summary>Dimensions used for all test vectors — kept small for fast test execution.</summary>
    public const int TestDimensions = 4;

    private SqliteVectorStore? _store;

    /// <summary>Gets the current <see cref="SqliteVectorStore"/> under test.</summary>
    public SqliteVectorStore Store => _store!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _store = CreateStore();
        // Trigger lazy schema init by issuing a no-op query
        await foreach (var _ in _store.QueryAsync(new float[TestDimensions], top: 0, CancellationToken.None).ConfigureAwait(false))
        {
            // intentionally empty — just warming up schema
        }
    }

    /// <inheritdoc />
    public async Task ResetAsync()
    {
        if (_store is not null)
        {
            await _store.DisposeAsync().ConfigureAwait(false);
        }

        _store = CreateStore();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_store is not null)
        {
            await _store.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static SqliteVectorStore CreateStore()
    {
        var options = new SqliteOptions
        {
            ConnectionString = "Data Source=:memory:",
            Dimensions = TestDimensions,
        };
        return new SqliteVectorStore(new OptionsWrapper<SqliteOptions>(options));
    }
}
