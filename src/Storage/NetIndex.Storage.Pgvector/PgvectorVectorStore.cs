using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector;
using Pgvector.Npgsql;
using NetIndex.Core.Abstractions;
using NetIndex.Storage.Pgvector.Options;

namespace NetIndex.Storage.Pgvector;

/// <summary>Vector store backed by PostgreSQL with the pgvector extension for cosine similarity search.</summary>
public sealed class PgvectorVectorStore : IVectorStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly int _dimensions;
    private volatile bool _initialized;

    // 0 = live, 1 = disposed. Interlocked.CompareExchange gives atomic claim-once semantics.
    private int _disposeState;

    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <summary>
    /// Serializes concurrent write operations in-process.
    /// PostgreSQL handles concurrent writes natively via MVCC, but we mirror the parity pattern
    /// established by <c>SqliteVectorStore</c> for consistency. Revisit when contention is measurable.
    /// </summary>
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <inheritdoc />
    public int Dimensions => _dimensions;

    /// <summary>Initializes with the configured pgvector options.</summary>
    /// <param name="options">Resolved pgvector options.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <c>Dimensions</c> is zero or negative.</exception>
    public PgvectorVectorStore(IOptions<PgvectorOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opt = options.Value;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            opt.Dimensions,
            $"{nameof(PgvectorOptions)}.{nameof(PgvectorOptions.Dimensions)}");
        _dimensions = opt.Dimensions;

        var builder = new NpgsqlDataSourceBuilder(opt.ConnectionString);
        builder.UseVector();
        _dataSource = builder.Build();
    }

    /// <inheritdoc />
    public async Task UpsertAsync(IEnumerable<RagChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ThrowIfDisposed();

        // Validate all chunks before opening any connection or transaction
        var chunkList = chunks.ToList();
        foreach (var chunk in chunkList)
        {
            ArgumentNullException.ThrowIfNull(chunk);
            ArgumentException.ThrowIfNullOrWhiteSpace(chunk.Id, nameof(chunk.Id));
            ArgumentException.ThrowIfNullOrWhiteSpace(chunk.DocumentId, nameof(chunk.DocumentId));
            ArgumentNullException.ThrowIfNull(chunk.Text, nameof(chunk.Text));
            if (chunk.Embedding is null)
            {
                throw new NetIndexStorageException(
                    "Chunk embedding is required for upsert.",
                    "PgvectorVectorStore",
                    "Upsert",
                    chunk.DocumentId);
            }

            if (chunk.Embedding.Length != _dimensions)
            {
                throw new NetIndexConfigurationException(
                    $"Embedding dimension mismatch: expected {_dimensions}, got {chunk.Embedding.Length}. " +
                    $"Ensure PgvectorOptions.Dimensions matches IEmbeddingGenerator.Dimensions.",
                    propertyName: "Dimensions",
                    expectedValue: _dimensions,
                    actualValue: chunk.Embedding.Length);
            }
        }

        if (chunkList.Count == 0)
        {
            return;
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            foreach (var chunk in chunkList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UpsertChunkAsync(chunk, connection, transaction, cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NetIndexStorageException)
        {
            throw;
        }
        catch (ObjectDisposedException) when (_disposeState != 0)
        {
            throw new ObjectDisposedException(nameof(PgvectorVectorStore));
        }
        catch (Exception ex)
        {
            throw WrapStorageException(ex, "Upsert");
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

        // Dimension validation before top guard so invalid vectors are always rejected
        if (queryVector.Length != _dimensions)
        {
            throw new NetIndexConfigurationException(
                $"Query vector dimension mismatch: expected {_dimensions}, got {queryVector.Length}.",
                propertyName: "Dimensions",
                expectedValue: _dimensions,
                actualValue: queryVector.Length);
        }

        if (top <= 0)
        {
            yield break;
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        List<SearchResult<RagChunk>> results;
        try
        {
            results = await ExecuteQueryAsync(queryVector, top, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NetIndexStorageException)
        {
            throw;
        }
        catch (ObjectDisposedException) when (_disposeState != 0)
        {
            throw new ObjectDisposedException(nameof(PgvectorVectorStore));
        }
        catch (Exception ex)
        {
            throw WrapStorageException(ex, "Query");
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
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM rag_chunks WHERE document_id = @documentId";
            cmd.Parameters.AddWithValue("documentId", documentId);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ObjectDisposedException) when (_disposeState != 0)
        {
            throw new ObjectDisposedException(nameof(PgvectorVectorStore));
        }
        catch (Exception ex)
        {
            throw WrapStorageException(ex, "Delete", documentId);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await _dataSource.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _initLock.Dispose();
            _writeLock.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposeState != 0)
        {
            throw new ObjectDisposedException(nameof(PgvectorVectorStore));
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

            await InitializeSchemaAsync(ct).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task InitializeSchemaAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS vector";
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // The data source's type catalog was snapshotted when this connection opened —
            // before the vector extension existed. Refresh so Pgvector.Vector parameters can be bound.
            await connection.ReloadTypesAsync().ConfigureAwait(false);

            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"""
                    CREATE TABLE IF NOT EXISTS rag_chunks (
                        id            SERIAL PRIMARY KEY,
                        chunk_id      TEXT UNIQUE NOT NULL,
                        document_id   TEXT NOT NULL,
                        text_content  TEXT NOT NULL,
                        metadata_json TEXT,
                        embedding     vector({_dimensions})
                    )
                    """;
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // Validate that the existing embedding column's dimension matches _dimensions.
            // Guards against deploying a different Dimensions value against an already-initialized schema.
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT atttypmod
                    FROM pg_attribute
                    WHERE attrelid = 'rag_chunks'::regclass
                      AND attname = 'embedding'
                      AND attnum > 0
                    """;
                var atttypmod = (int?)await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                if (atttypmod.HasValue && atttypmod.Value != -1)
                {
                    // pgvector stores the raw dimension in atttypmod (see vector_typmod_in in pgvector source)
                    var storedDimensions = atttypmod.Value;
                    if (storedDimensions != _dimensions)
                    {
                        throw new NetIndexConfigurationException(
                            $"PgvectorOptions.Dimensions mismatch: store was initialized with {storedDimensions} dimensions " +
                            $"but is now configured with {_dimensions}. " +
                            $"Drop and re-create the 'rag_chunks' table to change the embedding dimension.",
                            propertyName: "Dimensions",
                            expectedValue: storedDimensions,
                            actualValue: _dimensions);
                    }
                }
            }

            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"""
                    CREATE INDEX IF NOT EXISTS idx_rag_chunks_embedding
                    ON rag_chunks USING hnsw (embedding vector_cosine_ops)
                    """;
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "42501")
        {
            throw new NetIndexStorageException(
                "Permission denied creating the 'vector' extension. " +
                "A database administrator must run 'CREATE EXTENSION vector' once on this database, " +
                "or grant the connecting user the pg_extension_owner role.",
                "PgvectorVectorStore",
                "Initialize",
                null,
                ex);
        }
        catch (NetIndexConfigurationException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not NetIndexStorageException)
        {
            throw WrapStorageException(ex, "Initialize");
        }
    }

    private async Task UpsertChunkAsync(
        RagChunk chunk,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken ct)
    {
        var metaJson = chunk.Metadata is null ? null : JsonSerializer.Serialize(chunk.Metadata);
        var embedding = new Vector(chunk.Embedding!);

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO rag_chunks (chunk_id, document_id, text_content, metadata_json, embedding)
            VALUES (@chunkId, @documentId, @textContent, @metadataJson, @embedding)
            ON CONFLICT (chunk_id) DO UPDATE SET
                document_id   = EXCLUDED.document_id,
                text_content  = EXCLUDED.text_content,
                metadata_json = EXCLUDED.metadata_json,
                embedding     = EXCLUDED.embedding
            """;
        cmd.Parameters.AddWithValue("chunkId", chunk.Id);
        cmd.Parameters.AddWithValue("documentId", chunk.DocumentId);
        cmd.Parameters.AddWithValue("textContent", chunk.Text);
        cmd.Parameters.AddWithValue("metadataJson", (object?)metaJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("embedding", embedding);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<List<SearchResult<RagChunk>>> ExecuteQueryAsync(
        float[] queryVector, int top, CancellationToken ct)
    {
        var queryVec = new Vector(queryVector);
        var results = new List<SearchResult<RagChunk>>();

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT chunk_id, document_id, text_content, metadata_json,
                   1 - (embedding <=> @queryVec) AS score
            FROM rag_chunks
            ORDER BY embedding <=> @queryVec
            LIMIT @top
            """;
        cmd.Parameters.AddWithValue("queryVec", queryVec);
        cmd.Parameters.AddWithValue("top", top);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var chunkId = reader.GetString(0);
            var documentId = reader.GetString(1);
            var text = reader.GetString(2);
            var metaJson = await reader.IsDBNullAsync(3, ct).ConfigureAwait(false) ? null : reader.GetString(3);
            var score = (float)reader.GetDouble(4);

            IReadOnlyDictionary<string, string>? metadata = null;
            if (metaJson is not null)
            {
                var deserialized = JsonSerializer.Deserialize<Dictionary<string, string>>(metaJson);
                metadata = deserialized ?? throw new JsonException(
                    $"Metadata JSON for chunk '{chunkId}' deserialized to null unexpectedly.");
            }

            var chunk = new RagChunk(chunkId, text, null, documentId, metadata);
            results.Add(new SearchResult<RagChunk>(chunk, score, documentId));
        }

        return results;
    }

    private static NetIndexStorageException WrapStorageException(Exception ex, string operation, string? documentId = null)
    {
        var message = ex is JsonException
            ? $"JSON metadata error during {operation.ToLowerInvariant()}: {ex.Message}"
            : $"PostgreSQL {operation.ToLowerInvariant()} failed: {ex.Message}";
        return new NetIndexStorageException(message, "PgvectorVectorStore", operation, documentId, ex);
    }
}
