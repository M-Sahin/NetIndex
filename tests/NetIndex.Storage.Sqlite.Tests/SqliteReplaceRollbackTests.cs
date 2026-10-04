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
}
