using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NetIndex.Core.Abstractions;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="NetIndexPipeline"/> class.
    /// </summary>
    /// <param name="tenantResolver">Tenant resolver for authorization checks.</param>
    /// <param name="chunkingStrategy">Optional chunking strategy. Null uses pass-through default.</param>
    /// <param name="embeddingGenerator">Embedding generator for text vectors.</param>
    /// <param name="vectorStore">Vector store for persistence and similarity search.</param>
    /// <param name="chatClient">Chat client for LLM generation.</param>
    /// <param name="reranker">Optional reranker for post-retrieval scoring.</param>
    public NetIndexPipeline(
        ITenantResolver tenantResolver,
        IChunkingStrategy? chunkingStrategy,
        IEmbeddingGenerator embeddingGenerator,
        IVectorStore vectorStore,
        IChatClient chatClient,
        IDocumentReranker? reranker)
    {
        _tenantResolver = tenantResolver ?? throw new ArgumentNullException(nameof(tenantResolver));
        _chunkingStrategy = new(() => chunkingStrategy ?? new PassThroughChunkingStrategy());
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _reranker = reranker;
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

        await AuthorizeAsync(cancellationToken);

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
                var chunkId = $"{document.Id}_chunk_{i}";
                enrichedChunks.Add(
                    new RagChunk(chunkId, original.Text, embeddings[i], document.Id, original.Metadata));
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

        await AuthorizeAsync(cancellationToken);

        await foreach (var result in ExecuteQueryAsync(query, cancellationToken)
                       .WithCancellation(cancellationToken))
        {
            yield return result;
        }
    }

    private async IAsyncEnumerable<SearchResult<RagChunk>> ExecuteQueryAsync(
        string query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var queryVector = await _embeddingGenerator.GenerateAsync(query, cancellationToken);
        var results = _vectorStore.QueryAsync(queryVector, cancellationToken: cancellationToken);

        if (_reranker is not null)
        {
            var resultBuffer = new List<SearchResult<RagChunk>>();
            await foreach (var result in results.WithCancellation(cancellationToken))
            {
                resultBuffer.Add(result);
            }

            var reranked = await _reranker.RerankAsync(resultBuffer, query, cancellationToken);
            foreach (var rerankedResult in reranked)
            {
                yield return rerankedResult;
            }
        }
        else
        {
            await foreach (var result in results.WithCancellation(cancellationToken))
            {
                yield return result;
            }
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<GenerationChunk> GenerateAsync(
        string query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await AuthorizeAsync(cancellationToken);

        await foreach (var chunk in ExecuteGenerateAsync(query, cancellationToken)
                       .WithCancellation(cancellationToken))
        {
            yield return chunk;
        }
    }

    private async IAsyncEnumerable<GenerationChunk> ExecuteGenerateAsync(
        string query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var contextChunks = new List<RagChunk>();
        await foreach (var result in ExecuteQueryAsync(query, cancellationToken))
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
