using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NetIndex.Core.Abstractions;
using NetIndex.Core.Abstractions.Telemetry;
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
    {
        _tenantResolver = tenantResolver ?? throw new ArgumentNullException(nameof(tenantResolver));
        _chunkingStrategy = new(() => chunkingStrategy ?? new PassThroughChunkingStrategy());
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _reranker = reranker;
        _tenantFilteringOptions = tenantFilteringOptions ?? new TenantFilteringOptions();
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
        }
        catch (NetIndexException exception)
        {
            MarkActivityError(ingestActivity, exception, "NetIndex exception in ingest");
            throw;
        }
        catch (OperationCanceledException exception)
        {
            MarkActivityError(ingestActivity, exception, "Ingest cancelled");
            throw;
        }
        catch (Exception exception)
        {
            MarkActivityError(ingestActivity, exception);
            throw new NetIndexProviderException(
                "Ingestion pipeline failed.",
                exception is TimeoutException,
                null,
                "IngestionFailed",
                null,
                exception);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SearchResult<RagChunk>> QueryAsync(
        string query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var tenantId = await AuthorizeAsync(cancellationToken);

        await foreach (var result in ExecuteQueryAsync(query, tenantId, cancellationToken)
                       .WithCancellation(cancellationToken))
        {
            yield return result;
        }
    }

    private async IAsyncEnumerable<SearchResult<RagChunk>> ExecuteQueryAsync(
        string query,
        string tenantId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Embed span — no yield return inside this try/catch, so CS1626 does not apply.
        float[] queryVector;
        using (var embedActivity = NetIndexActivitySource.Source.StartActivity(NetIndexSpanNames.Embed, ActivityKind.Internal))
        {
            try
            {
                queryVector = await _embeddingGenerator.GenerateAsync(query, cancellationToken);
                embedActivity?.SetTag(NetIndexSpanTags.EmbeddingDimensions, queryVector.Length);
            }
            catch (Exception exception)
            {
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

        if (_reranker is not null)
        {
            // Scope the retrieve span tightly around buffering + filtering so it closes before yields.
            List<SearchResult<RagChunk>> rerankedFinal;
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
                    rerankedFinal = reranked.Take(TenantFilteringOptions.DefaultQueryTop).ToList();

                    retrieveActivity?.SetTag(NetIndexSpanTags.TenantId, tenantId);
                    retrieveActivity?.SetTag(NetIndexSpanTags.RetrieveTop, fetchTop);
                    retrieveActivity?.SetTag(NetIndexSpanTags.RetrieveResultCount, resultBuffer.Count);
                    retrieveActivity?.SetTag(NetIndexSpanTags.RetrieveFilteredCount, filteredCount);
                }
                catch (Exception exception)
                {
                    MarkActivityError(retrieveActivity, exception, "Retrieval failed");
                    throw;
                }
            }

            foreach (var r in rerankedFinal)
            {
                yield return r;
            }
        }
        else
        {
            // Scope the retrieve span tightly around buffering + filtering so it closes before yields.
            List<SearchResult<RagChunk>> finalResults;
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
                    finalResults = filtered.Take(TenantFilteringOptions.DefaultQueryTop).ToList();

                    retrieveActivity?.SetTag(NetIndexSpanTags.TenantId, tenantId);
                    retrieveActivity?.SetTag(NetIndexSpanTags.RetrieveTop, fetchTop);
                    retrieveActivity?.SetTag(NetIndexSpanTags.RetrieveResultCount, resultBuffer.Count);
                    retrieveActivity?.SetTag(NetIndexSpanTags.RetrieveFilteredCount, filtered.Count);
                }
                catch (Exception exception)
                {
                    MarkActivityError(retrieveActivity, exception, "Retrieval failed");
                    throw;
                }
            }

            foreach (var result in finalResults)
            {
                yield return result;
            }
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

        var tenantId = await AuthorizeAsync(cancellationToken);

        await foreach (var chunk in ExecuteGenerateAsync(query, tenantId, cancellationToken)
                       .WithCancellation(cancellationToken))
        {
            yield return chunk;
        }
    }

    private async IAsyncEnumerable<GenerationChunk> ExecuteGenerateAsync(
        string query,
        string tenantId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var generateActivity = NetIndexActivitySource.Source.StartActivity(NetIndexSpanNames.Generate, ActivityKind.Internal);
        generateActivity?.SetTag(NetIndexSpanTags.TenantId, tenantId);

        // Context-gathering phase: no yield return here so try/catch is valid (CS1626 does not apply).
        // If gathering fails, mark the generate span Error and dispose before rethrowing.
        List<RagChunk> contextChunks;
        try
        {
            contextChunks = new List<RagChunk>();
            await foreach (var result in ExecuteQueryAsync(query, tenantId, cancellationToken))
            {
                contextChunks.Add(result.Item);
            }
        }
        catch (Exception exception)
        {
            MarkActivityError(generateActivity, exception);
            generateActivity?.Dispose();
            throw;
        }

        generateActivity?.SetTag(NetIndexSpanTags.ContextChunkCount, contextChunks.Count);

        // The generate span spans the full chat generation duration and is disposed in the finally.
        var enumerator = _chatClient.GenerateStreamingAsync(query, contextChunks, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                GenerationChunk chunk;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        break;
                    }

                    chunk = enumerator.Current;
                }
                catch (Exception exception)
                {
                    MarkActivityError(generateActivity, exception);
                    throw;
                }

                yield return chunk;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
            generateActivity?.Dispose();
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
