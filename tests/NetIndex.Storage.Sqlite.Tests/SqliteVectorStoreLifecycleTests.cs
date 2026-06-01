using FluentAssertions;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Storage.Sqlite;
using NetIndex.Storage.Sqlite.Options;

namespace NetIndex.Storage.Sqlite.Tests;

/// <summary>Unit tests for <see cref="SqliteVectorStore"/> constructor validation and disposal lifecycle.</summary>
public class SqliteVectorStoreLifecycleTests
{
    private const int TestDimensions = 4;

    private static SqliteVectorStore CreateStore()
    {
        var options = new SqliteOptions
        {
            ConnectionString = "Data Source=:memory:",
            Dimensions = TestDimensions,
        };
        return new SqliteVectorStore(new OptionsWrapper<SqliteOptions>(options));
    }

    /// <summary>Constructor rejects zero Dimensions with ArgumentOutOfRangeException.</summary>
    [Fact]
    public void Ctor_WithZeroDimensions_ThrowsArgumentOutOfRangeException()
    {
        var options = new SqliteOptions
        {
            ConnectionString = "Data Source=:memory:",
            Dimensions = 0,
        };

        var act = () => new SqliteVectorStore(new OptionsWrapper<SqliteOptions>(options));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>Constructor rejects negative Dimensions with ArgumentOutOfRangeException.</summary>
    [Fact]
    public void Ctor_WithNegativeDimensions_ThrowsArgumentOutOfRangeException()
    {
        var options = new SqliteOptions
        {
            ConnectionString = "Data Source=:memory:",
            Dimensions = -1,
        };

        var act = () => new SqliteVectorStore(new OptionsWrapper<SqliteOptions>(options));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>DisposeAsync is idempotent — a second call must not throw.</summary>
    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrowAsync()
    {
        var store = CreateStore();

        await store.DisposeAsync();
        var act = async () => await store.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    /// <summary>UpsertAsync after Dispose throws ObjectDisposedException.</summary>
    [Fact]
    public async Task UpsertAsync_AfterDispose_ThrowsObjectDisposedExceptionAsync()
    {
        var store = CreateStore();
        await store.DisposeAsync();

        var act = async () => await store.UpsertAsync(Array.Empty<RagChunk>());

        await act.Should().ThrowAsync<ObjectDisposedException>()
            .Where(ex => ex.ObjectName == nameof(SqliteVectorStore));
    }

    /// <summary>QueryAsync after Dispose throws ObjectDisposedException on enumeration.</summary>
    [Fact]
    public async Task QueryAsync_AfterDispose_ThrowsObjectDisposedExceptionAsync()
    {
        var store = CreateStore();
        await store.DisposeAsync();

        var act = async () =>
        {
            await foreach (var _ in store.QueryAsync(new float[TestDimensions]))
            {
                // intentionally empty
            }
        };

        await act.Should().ThrowAsync<ObjectDisposedException>()
            .Where(ex => ex.ObjectName == nameof(SqliteVectorStore));
    }

    /// <summary>DeleteAsync after Dispose throws ObjectDisposedException.</summary>
    [Fact]
    public async Task DeleteAsync_AfterDispose_ThrowsObjectDisposedExceptionAsync()
    {
        var store = CreateStore();
        await store.DisposeAsync();

        var act = async () => await store.DeleteAsync("doc-1");

        await act.Should().ThrowAsync<ObjectDisposedException>()
            .Where(ex => ex.ObjectName == nameof(SqliteVectorStore));
    }

    /// <summary>
    /// Enumerator obtained before disposal throws the store's own ObjectDisposedException on
    /// first MoveNextAsync — not the raw SQLite one.
    /// </summary>
    [Fact]
    public async Task QueryAsync_HoldEnumeratorAcrossDispose_ThrowsObjectDisposedExceptionAsync()
    {
        var store = CreateStore();
        // Obtain the enumerator while the store is live (no DB call yet).
        var enumerator = store.QueryAsync(new float[TestDimensions]).GetAsyncEnumerator();
        await store.DisposeAsync();

        // First MoveNextAsync enters the iterator body, hits ThrowIfDisposed(), and throws.
        var act = async () => await enumerator.MoveNextAsync();

        await act.Should().ThrowAsync<ObjectDisposedException>()
            .Where(ex => ex.ObjectName == nameof(SqliteVectorStore));
    }
}
