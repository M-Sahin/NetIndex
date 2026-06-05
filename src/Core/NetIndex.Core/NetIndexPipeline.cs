using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetIndex.Core.Abstractions;
using NetIndex.Core.Abstractions.Telemetry;
using NetIndex.Core.Logging;
using NetIndex.Core.Options;

namespace NetIndex.Core;

/// <summary>
/// Default implementation of the RAG pipeline that coordinates ingest, query, and generate flows.
/// </summary>
public sealed class NetIndexPipeline : INetIndexPipeline
{
    private static readonly ChunkingOptions DefaultChunkingOptions =
        new(1000, 200, "\n\n");

    private readonly ITenantResolver _tenantResolver;
    private readonly Lazy<IChunkingStrategy> _chunkingStrategy;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly IVectorStore _vectorStore;
    private readonly IChatClient _chatClient;
    private readonly IDocumentReranker? _reranker;
    private readonly TenantFilteringOptions _tenantFilteringOptions;
    private readonly ILogger<NetIndexPipeline> _logger;

    private sealed class QueryLogMetrics
    {
        public int EmbeddingDimensions { get; set; }

        public int RetrieveTop { get; set; }

        public int ResultCount { get; set; }

        public int FilteredCount { get; set; }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NetIndexPipeline"/> class.
    /// </summary>
    /// <param name="tenantResolver">Tenant resolver for authorization checks.</param>
    /// <param name="chunkingStrategy">Optional chunking strategy. Null uses pass-through default.</param>
    /// <param name="embeddingGenerator">Embedding generator for text vectors.</param>
    /// <param name="vectorStore">Vector store for persistence and similarity search.</param>
    /// <param name="chatClient">Chat client for LLM generation.</param>
    /// <param name="reranker">Optional reranker for post-retrieval scoring.</param>
    /// <param name="tenantFilteringOptions">Optional tenant filtering options. Null uses defaults.</param>
    public NetIndexPipeline(
        ITenantResolver tenantResolver,
        IChunkingStrategy? chunkingStrategy,
        IEmbeddingGenerator embeddingGenerator,
        IVectorStore vectorStore,
        IChatClient chatClient,
        IDocumentReranker? reranker,
        TenantFilteringOptions? tenantFilteringOptions = null)
        : this(tenantResolver, chunkingStrategy, embeddingGenerator, vectorStore, chatClient, reranker, tenantFilteringOptions, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NetIndexPipeline"/> class.
    /// </summary>
    /// <param name="tenantResolver">Tenant resolver for authorization checks.</param>
    /// <param name="chunkingStrategy">Optional chunking strategy. Null uses pass-through default.</param>
    /// <param name="embeddingGenerator">Embedding generator for text vectors.</param>
    /// <param name="vectorStore">Vector store for persistence and similarity search.</param>
    /// <param name="chatClient">Chat client for LLM generation.</param>
    /// <param name="reranker">Optional reranker for post-retrieval scoring.</param>
    /// <param name="tenantFilteringOptions">Optional tenant filtering options. Null uses defaults.</param>
    /// <param name="logger">Optional logger. Null falls back to <see cref="NullLogger{T}"/>.</param>
    public NetIndexPipeline(
        ITenantResolver tenantResolver,
        IChunkingStrategy? chunkingStrategy,
        IEmbeddingGenerator embeddingGenerator,
        IVectorStore vectorStore,
        IChatClient chatClient,
        IDocumentReranker? reranker,
        TenantFilteringOptions? tenantFilteringOptions,
        ILogger<NetIndexPipeline>? logger)
    {
        _tenantResolver = tenantResolver ?? throw new ArgumentNullException(nameof(tenantResolver));
        _chunkingStrategy = new(() => chunkingStrategy ?? new PassThroughChunkingStrategy());
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _reranker = reranker;
        _tenantFilteringOptions = tenantFilteringOptions ?? new TenantFilteringOptions();
        _logger = logger ?? NullLogger<NetIndexPipeline>.Instance;
    }

    /// <summary>
    /// Authorizes the current request by resolving the tenant ID.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved tenant ID if authorization succeeds.</returns>
    /// <exception cref="NetIndexAuthorizationException">Thrown when authorization fails.</exception>
    internal async Task<string> AuthorizeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = await _tenantResolver.ResolveTenantIdAsync(cancellationToken);

            if (tenantId is null)
            {
                throw new NetIndexAuthorizationException(
                    "Tenant resolver returned null. Authorization denied.",
                    null, null, "NullTenantId");
            }

            return tenantId;
        }
        catch (NetIndexAuthorizationException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new NetIndexAuthorizationException(
                "Authorization failed during tenant resolution.",
                null, null, "TenantResolutionFailed", exception);
        }
    }

    /// <inheritdoc />
    public async Task IngestAsync(IDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var sw = Stopwatch.StartNew();
        using var ingestActivity = NetIndexActivitySource.Source.StartActivity(NetIndexSpanNames.Ingest, ActivityKind.Internal);
        ingestActivity?.SetTag(NetIndexSpanTags.DocumentId, document.Id);

        string tenantId;
        try
        {
            tenantId = await AuthorizeAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            MarkActivityError(ingestActivity, exception);
            NetIndexPipelineLogger.LogIngestFailed(_logger, sw.ElapsedMilliseconds, null, exception);
            throw;
        }

        ingestActivity?.SetTag(NetIndexSpanTags.TenantId, tenantId);

        try
        {
            var strategy = _chunkingStrategy.Value;

            List<RagChunk> chunkList;
            using (var chunkActivity = NetIndexActivitySource.Source.StartActivity(NetIndexSpanNames.Chunk, ActivityKind.Internal))
            {
                try
                {
                    var chunks = await strategy.ChunkAsync(document.Content, DefaultChunkingOptions, cancellationToken);
                    chunkList = chunks.ToList();
                    chunkActivity?.SetTag(NetIndexSpanTags.ChunkCount, chunkList.Count);
                }
                catch (Exception exception)
                {
                    MarkActivityError(chunkActivity, exception, "Chunking failed");
                    throw;
                }
            }

            var texts = chunkList.Select(c => c.Text).ToArray();
            float[][] embeddings;
            using (var embedActivity = NetIndexActivitySource.Source.StartActivity(NetIndexSpanNames.Embed, ActivityKind.Internal))
            {
                try
                {
                    embeddings = await _embeddingGenerator.GenerateBatchAsync(texts, cancellationToken);
                    embedActivity?.SetTag(NetIndexSpanTags.EmbeddingCount, embeddings.Length);
                    embedActivity?.SetTag(NetIndexSpanTags.EmbeddingDimensions, embeddings.Length > 0 ? embeddings[0].Length : 0);

                    if (embeddings.Length != chunkList.Count)
                    {
                        throw new NetIndexProviderException(
                            $"Embedding batch returned {embeddings.Length} vectors for {chunkList.Count} chunks.",
                            false, null, "EmbeddingBatchMismatch", null);
                    }
                }
                catch (Exception exception)
                {
                    MarkActivityError(embedActivity, exception, "Embedding generation failed");
                    throw;
                }
            }

            var enrichedChunks = new List<RagChunk>();
            for (var i = 0; i < chunkList.Count; i++)
            {
                var original = chunkList[i];

                // Guard against callers pre-setting the framework-reserved tenant-id key.
                // OrdinalIgnoreCase: "NETINDEX:TENANT_ID" is just as reserved as the canonical casing.
                if (original.Metadata is not null &&
                    original.Metadata.Keys.Contains(RagChunkMetadata.TenantId, StringComparer.OrdinalIgnoreCase))
                {
                    throw new NetIndexAuthorizationException(
                        $"Chunk metadata contains the reserved key '{RagChunkMetadata.TenantId}'. " +
                        "This key is framework-owned and must not be set by callers.",
                        tenantId: tenantId,
                        requiredClaim: null,
                        failureReason: "ReservedMetadataKeyConflict");
                }

                // Copy original metadata and stamp the tenant tag.
                var metadata = new Dictionary<string, string>(
                    original.Metadata ?? new Dictionary<string, string>())
                {
                    [RagChunkMetadata.TenantId] = tenantId,
                };

                var chunkId = $"{document.Id}_chunk_{i}";
                enrichedChunks.Add(
                    new RagChunk(chunkId, original.Text, embeddings[i], document.Id, metadata));
            }

            ingestActivity?.SetTag(NetIndexSpanTags.ChunkCount, enrichedChunks.Count);
            ingestActivity?.AddEvent(new ActivityEvent(
                "netindex.upsert",
                tags: new ActivityTagsCollection
                {
                    { NetIndexSpanTags.ChunkCount, enrichedChunks.Count },
                }));

            await _vectorStore.UpsertAsync(enrichedChunks, cancellationToken);

            NetIndexPipelineLogger.LogIngestSucceeded(
                _logger, sw.ElapsedMilliseconds, tenantId, document.Id,
                enrichedChunks.Count,
                embeddings.Length,
                embeddings.Length > 0 ? embeddings[0].Length : 0);
        }
        catch (NetIndexException exception)
        {
            MarkActivityError(ingestActivity, exception, "NetIndex exception in ingest");
            NetIndexPipelineLogger.LogIngestFailed(_logger, sw.ElapsedMilliseconds, tenantId, exception);
            throw;
        }
        catch (OperationCanceledException exception)
        {
            MarkActivityError(ingestActivity, exception, "Ingest cancelled");
            NetIndexPipelineLogger.LogIngestFailed(_logger, sw.ElapsedMilliseconds, tenantId, exception);
            throw;
        }
        catch (Exception exception)
        {
            MarkActivityError(ingestActivity, exception);
            var wrapped = new NetIndexProviderException(
                "Ingestion pipeline failed.",
                exception is TimeoutException,
                null,
                "IngestionFailed",
                null,
                exception);
            NetIndexPipelineLogger.LogIngestFailed(_logger, sw.ElapsedMilliseconds, tenantId, wrapped);
            throw wrapped;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SearchResult<RagChunk>> QueryAsync(
        string query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sw = Stopwatch.StartNew();

        // try/catch before any yield return is valid (CS1626 does not apply here).
        string tenantId;
        try
        {
            tenantId = await AuthorizeAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            NetIndexPipelineLogger.LogQueryFailed(_logger, sw.ElapsedMilliseconds, null, exception);
            throw;
        }

        var logMetrics = new QueryLogMetrics();
        await foreach (var result in ExecuteQueryAsync(query, tenantId, sw, shouldLog: true, logMetrics, cancellationToken)
                       .WithCancellation(cancellationToken))
        {
            yield return result;
        }
    }

    private async IAsyncEnumerable<SearchResult<RagChunk>> ExecuteQueryAsync(
        string query,
        string tenantId,
        Stopwatch sw,
        bool shouldLog,
        QueryLogMetrics logMetrics,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Embed span — no yield return inside this try/catch, so CS1626 does not apply.
        float[] queryVector;
        using (var embedActivity = NetIndexActivitySource.Source.StartActivity(NetIndexSpanNames.Embed, ActivityKind.Internal))
        {
            try
            {
                queryVector = await _embeddingGenerator.GenerateAsync(query, cancellationToken);
                logMetrics.EmbeddingDimensions = queryVector.Length;
                embedActivity?.SetTag(NetIndexSpanTags.EmbeddingDimensions, queryVector.Length);
            }
            catch (OperationCanceledException exception)
            {
                if (shouldLog)
                {
                    NetIndexPipelineLogger.LogQueryFailed(_logger, sw.ElapsedMilliseconds, tenantId, exception);
                }

                MarkActivityError(embedActivity, exception, "Query embedding failed");
                throw;
            }
            catch (Exception exception)
            {
                if (shouldLog)
                {
                    NetIndexPipelineLogger.LogQueryFailed(_logger, sw.ElapsedMilliseconds, tenantId, exception);
                }

                MarkActivityError(embedActivity, exception, "Query embedding failed");
                throw;
            }
        }

        // Over-fetch to compensate for cross-tenant chunks crowding the global top-K.
        // Clamp factor to >= 1 so a misconfigured 0 / negative value never produces fetchTop <= 0.
        // Widen the multiply to long so a large factor cannot overflow into a negative fetchTop
        // before MaxFetchCount caps it; the Math.Min result is always in [DefaultQueryTop, MaxFetchCount].
        var effectiveFactor = Math.Max(1, _tenantFilteringOptions.OverFetchFactor);
        var fetchTop = (int)Math.Min(
            (long)TenantFilteringOptions.DefaultQueryTop * effectiveFactor,
            TenantFilteringOptions.MaxFetchCount);
        logMetrics.RetrieveTop = fetchTop;

        List<SearchResult<RagChunk>> yieldResults = new();

        if (_reranker is not null)
        {
            // Scope the retrieve span tightly around buffering + filtering so it closes before yields.
            {
                using var retrieveActivity = NetIndexActivitySource.Source.StartActivity(NetIndexSpanNames.Retrieve, ActivityKind.Internal);
                try
                {
                    var resultBuffer = new List<SearchResult<RagChunk>>();
                    await foreach (var result in _vectorStore
                        .QueryAsync(queryVector, fetchTop, cancellationToken)
                        .WithCancellation(cancellationToken))
                    {
                        resultBuffer.Add(result);
                    }

                    // Filter to caller's tenant before reranking — never rerank another tenant's chunks.
                    // No cap before reranking: pass the full filtered pool so the reranker can score all
                    // candidates; cap to DefaultQueryTop only after reranking (AC-1 reranker-path fix).
                    var tenantBuffer = FilterByTenant(resultBuffer, tenantId);
                    var filteredCount = tenantBuffer.Count;
                    var reranked = await _reranker.RerankAsync(tenantBuffer, query, cancellationToken);
                    yieldResults = reranked.Take(TenantFilteringOptions.DefaultQueryTop).ToList();

                    logMetrics.ResultCount = resultBuffer.Count;
                    logMetrics.FilteredCount = filteredCount;

                    retrieveActivity?.SetTag(NetIndexSpanTags.TenantId, tenantId);
                    retrieveActivity?.SetTag(NetIndexSpanTags.RetrieveTop, fetchTop);
                    retrieveActivity?.SetTag(NetIndexSpanTags.RetrieveResultCount, resultBuffer.Count);
                    retrieveActivity?.SetTag(NetIndexSpanTags.RetrieveFilteredCount, filteredCount);
                }
                catch (OperationCanceledException exception)
                {
                    if (shouldLog)
                    {
                        NetIndexPipelineLogger.LogQueryFailed(_logger, sw.ElapsedMilliseconds, tenantId, exception);
                    }

                    MarkActivityError(retrieveActivity, exception, "Retrieval failed");
                    throw;
                }
                catch (Exception exception)
                {
                    if (shouldLog)
                    {
                        NetIndexPipelineLogger.LogQueryFailed(_logger, sw.ElapsedMilliseconds, tenantId, exception);
                    }

                    MarkActivityError(retrieveActivity, exception, "Retrieval failed");
                    throw;
                }
            }
        }
        else
        {
            // Scope the retrieve span tightly around buffering + filtering so it closes before yields.
            {
                using var retrieveActivity = NetIndexActivitySource.Source.StartActivity(NetIndexSpanNames.Retrieve, ActivityKind.Internal);
                try
                {
                    var resultBuffer = new List<SearchResult<RagChunk>>();
                    await foreach (var result in _vectorStore
                        .QueryAsync(queryVector, fetchTop, cancellationToken)
                        .WithCancellation(cancellationToken))
                    {
                        resultBuffer.Add(result);
                    }

                    var filtered = FilterByTenant(resultBuffer, tenantId);
                    yieldResults = filtered.Take(TenantFilteringOptions.DefaultQueryTop).ToList();

                    logMetrics.ResultCount = resultBuffer.Count;
                    logMetrics.FilteredCount = filtered.Count;

                    retrieveActivity?.SetTag(NetIndexSpanTags.TenantId, tenantId);
                    retrieveActivity?.SetTag(NetIndexSpanTags.RetrieveTop, fetchTop);
                    retrieveActivity?.SetTag(NetIndexSpanTags.RetrieveResultCount, resultBuffer.Count);
                    retrieveActivity?.SetTag(NetIndexSpanTags.RetrieveFilteredCount, filtered.Count);
                }
                catch (OperationCanceledException exception)
                {
                    if (shouldLog)
                    {
                        NetIndexPipelineLogger.LogQueryFailed(_logger, sw.ElapsedMilliseconds, tenantId, exception);
                    }

                    MarkActivityError(retrieveActivity, exception, "Retrieval failed");
                    throw;
                }
                catch (Exception exception)
                {
                    if (shouldLog)
                    {
                        NetIndexPipelineLogger.LogQueryFailed(_logger, sw.ElapsedMilliseconds, tenantId, exception);
                    }

                    MarkActivityError(retrieveActivity, exception, "Retrieval failed");
                    throw;
                }
            }
        }

        // Log success after all retrieval is complete and before yielding results.
        if (shouldLog)
        {
            NetIndexPipelineLogger.LogQuerySucceeded(_logger, sw.ElapsedMilliseconds, tenantId,
                logMetrics.EmbeddingDimensions, logMetrics.RetrieveTop, logMetrics.ResultCount, logMetrics.FilteredCount);
        }

        foreach (var result in yieldResults)
        {
            yield return result;
        }
    }

    private static List<SearchResult<RagChunk>> FilterByTenant(
        List<SearchResult<RagChunk>> results,
        string tenantId)
    {
        var filtered = new List<SearchResult<RagChunk>>(results.Count);
        foreach (var result in results)
        {
            if (result.Item.Metadata is not null &&
                result.Item.Metadata.TryGetValue(RagChunkMetadata.TenantId, out var tag) &&
                string.Equals(tag, tenantId, StringComparison.Ordinal))
            {
                filtered.Add(result);
            }
        }

        // Results from the store are already score-ordered; preserve that order.
        return filtered;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<GenerationChunk> GenerateAsync(
        string query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sw = Stopwatch.StartNew();

        // try/catch before any yield return is valid (CS1626 does not apply here).
        string tenantId;
        try
        {
            tenantId = await AuthorizeAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            NetIndexPipelineLogger.LogGenerateFailed(_logger, sw.ElapsedMilliseconds, null, exception);
            throw;
        }

        await foreach (var chunk in ExecuteGenerateAsync(query, tenantId, sw, cancellationToken)
                       .WithCancellation(cancellationToken))
        {
            yield return chunk;
        }
    }

    private async IAsyncEnumerable<GenerationChunk> ExecuteGenerateAsync(
        string query,
        string tenantId,
        Stopwatch sw,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var generateActivity = NetIndexActivitySource.Source.StartActivity(NetIndexSpanNames.Generate, ActivityKind.Internal);
        generateActivity?.SetTag(NetIndexSpanTags.TenantId, tenantId);

        // Context-gathering phase: no yield return here so try/catch is valid (CS1626 does not apply).
        // If gathering fails, mark the generate span Error and dispose before rethrowing.
        List<RagChunk> contextChunks;
        QueryLogMetrics queryLogMetrics;
        try
        {
            contextChunks = new List<RagChunk>();
            queryLogMetrics = new QueryLogMetrics();
            await foreach (var result in ExecuteQueryAsync(query, tenantId, sw, shouldLog: false, queryLogMetrics, cancellationToken))
            {
                contextChunks.Add(result.Item);
            }
        }
        catch (OperationCanceledException exception)
        {
            MarkActivityError(generateActivity, exception);
            generateActivity?.Dispose();
            NetIndexPipelineLogger.LogGenerateFailed(_logger, sw.ElapsedMilliseconds, tenantId, exception);
            throw;
        }
        catch (Exception exception)
        {
            MarkActivityError(generateActivity, exception);
            generateActivity?.Dispose();
            NetIndexPipelineLogger.LogGenerateFailed(_logger, sw.ElapsedMilliseconds, tenantId, exception);
            throw;
        }

        generateActivity?.SetTag(NetIndexSpanTags.EmbeddingDimensions, queryLogMetrics.EmbeddingDimensions);
        generateActivity?.SetTag(NetIndexSpanTags.RetrieveTop, queryLogMetrics.RetrieveTop);
        generateActivity?.SetTag(NetIndexSpanTags.RetrieveResultCount, queryLogMetrics.ResultCount);
        generateActivity?.SetTag(NetIndexSpanTags.RetrieveFilteredCount, queryLogMetrics.FilteredCount);
        generateActivity?.SetTag(NetIndexSpanTags.ContextChunkCount, contextChunks.Count);

        await foreach (var chunk in ExecuteGenerateStreamAsync(
                           query,
                           tenantId,
                           sw,
                           generateActivity,
                           contextChunks,
                           queryLogMetrics,
                           cancellationToken)
                       .WithCancellation(cancellationToken))
        {
            yield return chunk;
        }
    }

    private async IAsyncEnumerable<GenerationChunk> ExecuteGenerateStreamAsync(
        string query,
        string tenantId,
        Stopwatch sw,
        Activity? generateActivity,
        List<RagChunk> contextChunks,
        QueryLogMetrics queryLogMetrics,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerator<GenerationChunk>? enumerator = null;
        var completedNaturally = false;
        var loggedError = false;

        try
        {
            try
            {
                enumerator = _chatClient.GenerateStreamingAsync(query, contextChunks, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);
            }
            catch (OperationCanceledException exception)
            {
                loggedError = true;
                MarkActivityError(generateActivity, exception);
                NetIndexPipelineLogger.LogGenerateFailed(_logger, sw.ElapsedMilliseconds, tenantId, exception);
                throw;
            }
            catch (Exception exception)
            {
                loggedError = true;
                MarkActivityError(generateActivity, exception);
                NetIndexPipelineLogger.LogGenerateFailed(_logger, sw.ElapsedMilliseconds, tenantId, exception);
                throw;
            }

            while (true)
            {
                GenerationChunk chunk;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        completedNaturally = true;
                        break;
                    }

                    chunk = enumerator.Current;
                }
                catch (OperationCanceledException exception)
                {
                    loggedError = true;
                    MarkActivityError(generateActivity, exception);
                    NetIndexPipelineLogger.LogGenerateFailed(_logger, sw.ElapsedMilliseconds, tenantId, exception);
                    throw;
                }
                catch (Exception exception)
                {
                    loggedError = true;
                    MarkActivityError(generateActivity, exception);
                    NetIndexPipelineLogger.LogGenerateFailed(_logger, sw.ElapsedMilliseconds, tenantId, exception);
                    throw;
                }

                yield return chunk;
            }
        }
        finally
        {
            try
            {
                if (enumerator is not null)
                {
                    try
                    {
                        await enumerator.DisposeAsync();
                    }
                    catch (Exception exception)
                    {
                        if (!loggedError)
                        {
                            loggedError = true;
                            MarkActivityError(generateActivity, exception);
                            NetIndexPipelineLogger.LogGenerateFailed(_logger, sw.ElapsedMilliseconds, tenantId, exception);
                            throw;
                        }
                    }
                }

                if (!loggedError)
                {
                    if (completedNaturally)
                    {
                        NetIndexPipelineLogger.LogGenerateSucceeded(
                            _logger,
                            sw.ElapsedMilliseconds,
                            tenantId,
                            queryLogMetrics.EmbeddingDimensions,
                            queryLogMetrics.RetrieveTop,
                            queryLogMetrics.ResultCount,
                            queryLogMetrics.FilteredCount,
                            contextChunks.Count);
                    }
                    else
                    {
                        NetIndexPipelineLogger.LogGenerateCanceled(_logger, sw.ElapsedMilliseconds, tenantId);
                    }
                }
            }
            finally
            {
                generateActivity?.Dispose();
            }
        }
    }

    private static void MarkActivityError(Activity? activity, Exception exception, string? description = null)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, description ?? exception.Message);
        activity.AddEvent(new ActivityEvent(
            "exception",
            tags: new ActivityTagsCollection
            {
                { "exception.type", exception.GetType().FullName },
                { "exception.message", exception.Message },
            }));
    }
}
