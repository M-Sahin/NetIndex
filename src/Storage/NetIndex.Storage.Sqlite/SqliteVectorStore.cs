using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Storage.Sqlite.Options;

namespace NetIndex.Storage.Sqlite;

/// <summary>Vector store backed by SQLite with the sqlite-vec extension for cosine similarity search.</summary>
public sealed class SqliteVectorStore : IVectorStore, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly int _dimensions;
    private volatile bool _initialized;
    private volatile bool _disposed;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <inheritdoc />
    public int Dimensions => _dimensions;

    /// <summary>Initializes with the configured SQLite options.</summary>
    /// <param name="options">Resolved SQLite options.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public SqliteVectorStore(IOptions<SqliteOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opt = options.Value;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            opt.Dimensions,
            $"{nameof(SqliteOptions)}.{nameof(SqliteOptions.Dimensions)}");
        _dimensions = opt.Dimensions;
        _connection = new SqliteConnection(opt.ConnectionString);
    }

    /// <inheritdoc />
    public async Task UpsertAsync(IEnumerable<RagChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ThrowIfDisposed();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        // Validate all chunks before opening any transaction
        var chunkList = chunks.ToList();
        foreach (var chunk in chunkList)
        {
            ArgumentNullException.ThrowIfNull(chunk);
            if (chunk.Embedding is null)
            {
                throw new NetIndexStorageException(
                    "Chunk embedding is required for upsert.",
                    "SqliteVectorStore",
                    "Upsert",
                    chunk.DocumentId);
            }

            if (chunk.Embedding.Length != _dimensions)
            {
                throw new NetIndexConfigurationException(
                    $"Embedding dimension mismatch: expected {_dimensions}, got {chunk.Embedding.Length}. " +
                    $"Ensure SqliteOptions.Dimensions matches IEmbeddingGenerator.Dimensions.",
                    propertyName: "Dimensions",
                    expectedValue: _dimensions,
                    actualValue: chunk.Embedding.Length);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var transaction = _connection.BeginTransaction();
            foreach (var chunk in chunkList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UpsertChunkAsync(chunk, transaction).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            throw new ObjectDisposedException(nameof(SqliteVectorStore));
        }
        catch (SqliteException ex)
        {
            throw new NetIndexStorageException(
                $"SQLite upsert failed: {ex.Message}",
                "SqliteVectorStore",
                "Upsert",
                null,
                ex);
        }
        catch (JsonException ex)
        {
            throw new NetIndexStorageException(
                $"Failed to serialize chunk metadata: {ex.Message}",
                "SqliteVectorStore",
                "Upsert",
                null,
                ex);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SearchResult<RagChunk>> QueryAsync(
        float[] queryVector,
        int top = 5,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryVector);
        ThrowIfDisposed();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (top <= 0)
        {
            yield break;
        }

        if (queryVector.Length != _dimensions)
        {
            throw new NetIndexConfigurationException(
                $"Query vector dimension mismatch: expected {_dimensions}, got {queryVector.Length}.",
                propertyName: "Dimensions",
                expectedValue: _dimensions,
                actualValue: queryVector.Length);
        }

        List<SearchResult<RagChunk>> results;
        try
        {
            results = await ExecuteQueryAsync(queryVector, top, cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            throw new ObjectDisposedException(nameof(SqliteVectorStore));
        }
        catch (SqliteException ex)
        {
            throw new NetIndexStorageException(
                $"SQLite query failed: {ex.Message}",
                "SqliteVectorStore",
                "Query",
                null,
                ex);
        }
        catch (JsonException ex)
        {
            throw new NetIndexStorageException(
                $"Failed to deserialize chunk metadata: {ex.Message}",
                "SqliteVectorStore",
                "Query",
                null,
                ex);
        }

        foreach (var result in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return result;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ThrowIfDisposed();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var transaction = _connection.BeginTransaction();

            // Collect rowids inside the transaction (no TOCTOU window with concurrent writes)
            var rowids = new List<long>();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "SELECT rowid FROM rag_chunks WHERE document_id = @documentId";
                cmd.Parameters.AddWithValue("@documentId", documentId);
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    rowids.Add(reader.GetInt64(0));
                }
            }

            if (rowids.Count == 0)
            {
                return;
            }

            // Delete vectors first
            foreach (var rowid in rowids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var vecCmd = _connection.CreateCommand();
                vecCmd.Transaction = transaction;
                vecCmd.CommandText = "DELETE FROM rag_chunks_vec WHERE rowid = @rowid";
                vecCmd.Parameters.AddWithValue("@rowid", rowid);
                await vecCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // Delete metadata
            using (var metaCmd = _connection.CreateCommand())
            {
                metaCmd.Transaction = transaction;
                metaCmd.CommandText = "DELETE FROM rag_chunks WHERE document_id = @documentId";
                metaCmd.Parameters.AddWithValue("@documentId", documentId);
                await metaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            throw new ObjectDisposedException(nameof(SqliteVectorStore));
        }
        catch (SqliteException ex)
        {
            throw new NetIndexStorageException(
                $"SQLite delete failed: {ex.Message}",
                "SqliteVectorStore",
                "Delete",
                documentId,
                ex);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _connection.Dispose();
        _initLock.Dispose();
        _writeLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SqliteVectorStore));
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await InitializeSchemaAsync().ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task InitializeSchemaAsync()
    {
        try
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync().ConfigureAwait(false);
            }
            _connection.LoadExtension("vec0");
        }
        catch (SqliteException ex)
        {
            throw new NetIndexStorageException(
                $"Failed to initialize SQLite or load sqlite-vec extension. " +
                $"Ensure the 'sqlite-vec' NuGet package is referenced. Details: {ex.Message}",
                "SqliteVectorStore",
                "Initialize",
                null,
                ex);
        }

        // Metadata table
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS rag_chunks (
                    rowid         INTEGER PRIMARY KEY AUTOINCREMENT,
                    chunk_id      TEXT NOT NULL UNIQUE,
                    document_id   TEXT NOT NULL,
                    text_content  TEXT NOT NULL,
                    metadata_json TEXT
                )
                """;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        // vec0 virtual table — dimension and metric are baked in at creation time
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = $"""
                CREATE VIRTUAL TABLE IF NOT EXISTS rag_chunks_vec USING vec0(
                    embedding float[{_dimensions}] distance_metric=cosine
                )
                """;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private async Task UpsertChunkAsync(RagChunk chunk, SqliteTransaction transaction)
    {
        var embBytes = ToBytes(chunk.Embedding!);
        var metaJson = chunk.Metadata is null ? null : JsonSerializer.Serialize(chunk.Metadata);

        // Check if this chunk already exists
        long? existingRowid = null;
        using (var selectCmd = _connection.CreateCommand())
        {
            selectCmd.Transaction = transaction;
            selectCmd.CommandText = "SELECT rowid FROM rag_chunks WHERE chunk_id = @chunkId";
            selectCmd.Parameters.AddWithValue("@chunkId", chunk.Id);
            var result = await selectCmd.ExecuteScalarAsync().ConfigureAwait(false);
            if (result is not null and not DBNull)
            {
                existingRowid = Convert.ToInt64(result);
            }
        }

        if (existingRowid.HasValue)
        {
            // Delete old vector (vec0 has no UPDATE)
            using (var delVecCmd = _connection.CreateCommand())
            {
                delVecCmd.Transaction = transaction;
                delVecCmd.CommandText = "DELETE FROM rag_chunks_vec WHERE rowid = @rowid";
                delVecCmd.Parameters.AddWithValue("@rowid", existingRowid.Value);
                await delVecCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            // Update metadata in-place (preserves rowid)
            using (var updCmd = _connection.CreateCommand())
            {
                updCmd.Transaction = transaction;
                updCmd.CommandText = """
                    UPDATE rag_chunks
                    SET text_content = @text, document_id = @docId, metadata_json = @meta
                    WHERE chunk_id = @chunkId
                    """;
                updCmd.Parameters.AddWithValue("@text", chunk.Text);
                updCmd.Parameters.AddWithValue("@docId", chunk.DocumentId);
                updCmd.Parameters.AddWithValue("@meta", (object?)metaJson ?? DBNull.Value);
                updCmd.Parameters.AddWithValue("@chunkId", chunk.Id);
                await updCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            // Re-insert vector with same rowid
            using var insVecCmd = _connection.CreateCommand();
            insVecCmd.Transaction = transaction;
            insVecCmd.CommandText = "INSERT INTO rag_chunks_vec (rowid, embedding) VALUES (@rowid, @emb)";
            insVecCmd.Parameters.AddWithValue("@rowid", existingRowid.Value);
            insVecCmd.Parameters.AddWithValue("@emb", embBytes);
            await insVecCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        else
        {
            // Insert metadata — get new rowid
            long newRowid;
            using (var insCmd = _connection.CreateCommand())
            {
                insCmd.Transaction = transaction;
                insCmd.CommandText = """
                    INSERT INTO rag_chunks (chunk_id, document_id, text_content, metadata_json)
                    VALUES (@chunkId, @docId, @text, @meta);
                    SELECT last_insert_rowid();
                    """;
                insCmd.Parameters.AddWithValue("@chunkId", chunk.Id);
                insCmd.Parameters.AddWithValue("@docId", chunk.DocumentId);
                insCmd.Parameters.AddWithValue("@text", chunk.Text);
                insCmd.Parameters.AddWithValue("@meta", (object?)metaJson ?? DBNull.Value);
                var result = await insCmd.ExecuteScalarAsync().ConfigureAwait(false);
                newRowid = Convert.ToInt64(result);
            }

            // Insert vector with matching rowid
            using var insVecCmd = _connection.CreateCommand();
            insVecCmd.Transaction = transaction;
            insVecCmd.CommandText = "INSERT INTO rag_chunks_vec (rowid, embedding) VALUES (@rowid, @emb)";
            insVecCmd.Parameters.AddWithValue("@rowid", newRowid);
            insVecCmd.Parameters.AddWithValue("@emb", embBytes);
            await insVecCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private async Task<List<SearchResult<RagChunk>>> ExecuteQueryAsync(
        float[] queryVector, int top, CancellationToken ct)
    {
        var queryBytes = ToBytes(queryVector);
        var results = new List<SearchResult<RagChunk>>();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT c.chunk_id, c.document_id, c.text_content, c.metadata_json, v.distance
            FROM rag_chunks_vec v
            JOIN rag_chunks c ON c.rowid = v.rowid
            WHERE v.embedding MATCH @queryVec
              AND k = @top
            ORDER BY v.distance
            """;
        cmd.Parameters.AddWithValue("@queryVec", queryBytes);
        cmd.Parameters.AddWithValue("@top", top);

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var chunkId = reader.GetString(0);
            var documentId = reader.GetString(1);
            var text = reader.GetString(2);
            var metaJson = await reader.IsDBNullAsync(3).ConfigureAwait(false) ? null : reader.GetString(3);
            var distance = reader.GetDouble(4);
            var score = 1f - (float)distance;

            IReadOnlyDictionary<string, string>? metadata = null;
            if (metaJson is not null)
            {
                metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metaJson);
            }

            var chunk = new RagChunk(chunkId, text, null, documentId, metadata);
            results.Add(new SearchResult<RagChunk>(chunk, score, documentId));
        }

        return results;
    }

    private static byte[] ToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
