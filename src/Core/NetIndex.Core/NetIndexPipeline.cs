using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NetIndex.Core.Abstractions;
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

        var tenantId = await AuthorizeAsync(cancellationToken);

        try
        {
            var strategy = _chunkingStrategy.Value;
            var chunks = await strategy.ChunkAsync(
                document.Content,
                DefaultChunkingOptions,
                cancellationToken);

            var chunkList = chunks.ToList();
            var texts = chunkList.Select(c => c.Text).ToArray();
            var embeddings = await _embeddingGenerator.GenerateBatchAsync(texts, cancellationToken);

            if (embeddings.Length != chunkList.Count)
            {
                throw new NetIndexProviderException(
                    $"Embedding batch returned {embeddings.Length} vectors for {chunkList.Count} chunks.",
                    false, null, "EmbeddingBatchMismatch", null);
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

            await _vectorStore.UpsertAsync(enrichedChunks, cancellationToken);
        }
        catch (NetIndexException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
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
        var queryVector = await _embeddingGenerator.GenerateAsync(query, cancellationToken);

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
            var reranked = await _reranker.RerankAsync(tenantBuffer, query, cancellationToken);
            foreach (var rerankedResult in reranked.Take(TenantFilteringOptions.DefaultQueryTop))
            {
                yield return rerankedResult;
            }
        }
        else
        {
            var resultBuffer = new List<SearchResult<RagChunk>>();
            await foreach (var result in _vectorStore
                .QueryAsync(queryVector, fetchTop, cancellationToken)
                .WithCancellation(cancellationToken))
            {
                resultBuffer.Add(result);
            }

            foreach (var result in FilterByTenant(resultBuffer, tenantId)
                         .Take(TenantFilteringOptions.DefaultQueryTop))
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
        var contextChunks = new List<RagChunk>();
        await foreach (var result in ExecuteQueryAsync(query, tenantId, cancellationToken))
        {
            contextChunks.Add(result.Item);
        }

        await foreach (var chunk in _chatClient.GenerateStreamingAsync(
                         query, contextChunks, cancellationToken))
        {
            yield return chunk;
        }
    }
}
