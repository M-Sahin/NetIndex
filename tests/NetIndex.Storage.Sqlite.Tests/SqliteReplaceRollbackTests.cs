using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Storage.Sqlite.Options;

namespace NetIndex.Storage.Sqlite.Tests;

/// <summary>A failure inside the replace transaction rolls back and keeps the old chunk set.</summary>
public class SqliteReplaceRollbackTests
{
    /// <summary>A BEFORE INSERT trigger aborts mid-transaction (after the delete); the old set must survive.</summary>
    [Fact]
    public async Task Replace_FailureMidTransaction_RollsBackAndKeepsTheOldSetAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netindex-rollback-{Guid.NewGuid():N}.db");
        SqliteConnection.ClearAllPools();
        try
        {
            var options = new SqliteOptions { ConnectionString = $"Data Source={path}", Dimensions = 4 };
            await using var store = new SqliteVectorStore(new OptionsWrapper<SqliteOptions>(options));
            var vector = new float[] { 1f, 0f, 0f, 0f };
            var tenant = new Dictionary<string, string> { [RagChunkMetadata.TenantId] = "tenant-a" };

            await store.ReplaceDocumentAsync(
                "doc-rb",
                Enumerable.Range(0, 3).Select(i => new RagChunk($"doc-rb_chunk_{i}", $"old-{i}", vector, "doc-rb", tenant)),
                CancellationToken.None);

            using (var other = new SqliteConnection($"Data Source={path}"))
            {
                await other.OpenAsync();
                using var cmd = other.CreateCommand();
                cmd.CommandText = """
                    CREATE TRIGGER poison BEFORE INSERT ON rag_chunks
                    WHEN NEW.text_content = 'POISON'
                    BEGIN SELECT RAISE(ABORT, 'poisoned'); END;
                    """;
                await cmd.ExecuteNonQueryAsync();
            }

            var exception = await Record.ExceptionAsync(() => store.ReplaceDocumentAsync(
                "doc-rb",
                new[]
                {
                    new RagChunk("doc-rb_chunk_0", "new-0", vector, "doc-rb", tenant),
                    new RagChunk("doc-rb_chunk_1", "POISON", vector, "doc-rb", tenant),
                },
                CancellationToken.None));

            Assert.IsType<NetIndexStorageException>(exception);
            var texts = new List<string>();
            await foreach (var hit in store.QueryAsync(vector, top: 10, CancellationToken.None))
            {
                texts.Add(hit.Item.Text);
            }

            Assert.Equal(new[] { "old-0", "old-1", "old-2" }, texts.OrderBy(t => t, StringComparer.Ordinal).ToArray());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    /// <summary>Story 2.14: a BEFORE DELETE trigger aborts mid-delete (after the first row went); every chunk must survive.</summary>
    [Fact]
    public async Task Delete_FailureMidTransaction_RollsBackAndKeepsTheSetAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netindex-delete-rollback-{Guid.NewGuid():N}.db");
        SqliteConnection.ClearAllPools();
        try
        {
            var options = new SqliteOptions { ConnectionString = $"Data Source={path}", Dimensions = 4 };
            await using var store = new SqliteVectorStore(new OptionsWrapper<SqliteOptions>(options));
            var vector = new float[] { 1f, 0f, 0f, 0f };
            var tenant = new Dictionary<string, string> { [RagChunkMetadata.TenantId] = "tenant-a" };
            var texts = new[] { "keep-0", "POISON", "keep-2" };

            await store.ReplaceDocumentAsync(
                "doc-del",
                texts.Select((t, i) => new RagChunk($"doc-del_chunk_{i}", t, vector, "doc-del", tenant)),
                CancellationToken.None);

            using (var other = new SqliteConnection($"Data Source={path}"))
            {
                await other.OpenAsync();
                using var cmd = other.CreateCommand();
                cmd.CommandText = """
                    CREATE TRIGGER poison_delete BEFORE DELETE ON rag_chunks
                    WHEN OLD.text_content = 'POISON'
                    BEGIN SELECT RAISE(ABORT, 'poisoned'); END;
                    """;
                await cmd.ExecuteNonQueryAsync();
            }

            var exception = await Record.ExceptionAsync(() => store.DeleteDocumentAsync("doc-del", "tenant-a", CancellationToken.None));

            Assert.IsType<NetIndexStorageException>(exception);
            var remaining = new List<string>();
            await foreach (var hit in store.QueryAsync(vector, top: 10, CancellationToken.None))
            {
                remaining.Add(hit.Item.Text);
            }

            Assert.Equal(texts.OrderBy(t => t, StringComparer.Ordinal), remaining.OrderBy(t => t, StringComparer.Ordinal));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Story 2.14: a tenant-scoped delete removes the vectors too, not only the metadata rows. The deleted
    /// document's vectors are exactly the query vector, so any orphan left in rag_chunks_vec would take the
    /// top-k KNN slots and the (joined) query would come back short.
    /// </summary>
    [Fact]
    public async Task Delete_RemovesTheVectorRows_SoNoOrphanStealsKnnSlotsAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netindex-delete-vec-{Guid.NewGuid():N}.db");
        SqliteConnection.ClearAllPools();
        try
        {
            var options = new SqliteOptions { ConnectionString = $"Data Source={path}", Dimensions = 4 };
            await using var store = new SqliteVectorStore(new OptionsWrapper<SqliteOptions>(options));
            var query = new float[] { 1f, 0f, 0f, 0f };
            var nearby = new float[] { 0.9f, 0.1f, 0f, 0f };
            var tenant = new Dictionary<string, string> { [RagChunkMetadata.TenantId] = "tenant-a" };

            await store.ReplaceDocumentAsync(
                "doc-gone",
                Enumerable.Range(0, 3).Select(i => new RagChunk($"doc-gone_chunk_{i}", $"g-{i}", query, "doc-gone", tenant)),
                CancellationToken.None);
            await store.ReplaceDocumentAsync(
                "doc-kept",
                Enumerable.Range(0, 2).Select(i => new RagChunk($"doc-kept_chunk_{i}", $"k-{i}", nearby, "doc-kept", tenant)),
                CancellationToken.None);

            await store.DeleteDocumentAsync("doc-gone", "tenant-a", CancellationToken.None);

            var hits = new List<string>();
            await foreach (var hit in store.QueryAsync(query, top: 2, CancellationToken.None))
            {
                hits.Add(hit.Item.Text);
            }

            Assert.Equal(new[] { "k-0", "k-1" }, hits.OrderBy(t => t, StringComparer.Ordinal).ToArray());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
