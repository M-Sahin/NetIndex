using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using NetIndex.Core.Abstractions;

namespace NetIndex.SemanticKernel.Internal;

/// <summary>
/// Reflected by <see cref="KernelPluginFactory"/> into the <c>NetIndex</c> Semantic Kernel plugin.
/// Every function delegates to the configured <see cref="INetIndexPipeline"/>; authorization,
/// tenant isolation, and provider behavior remain owned by NetIndex.
/// </summary>
[Description("Retrieval-augmented generation tools backed by a NetIndex pipeline.")]
internal sealed class NetIndexPlugin
{
    private readonly INetIndexPipeline _pipeline;

    public NetIndexPlugin(INetIndexPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    [KernelFunction("RetrieveChunks")]
    [Description("Retrieves the document chunks most relevant to a query from the NetIndex index, ordered by relevance.")]
    public async Task<IReadOnlyList<NetIndexRetrievedChunk>> RetrieveChunksAsync(
        [Description("The search query text.")] string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var chunks = new List<NetIndexRetrievedChunk>();
        await foreach (var result in _pipeline.QueryAsync(query, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            IReadOnlyDictionary<string, string> metadata = result.Item.Metadata switch
            {
                Dictionary<string, string> dictionary => new Dictionary<string, string>(dictionary, dictionary.Comparer),
                { } other => new Dictionary<string, string>(other),
                null => new Dictionary<string, string>()
            };

            chunks.Add(new NetIndexRetrievedChunk(
                result.Item.Id,
                result.DocumentId,
                result.Item.Text,
                result.Score,
                metadata));
        }

        return chunks;
    }

    [KernelFunction("IngestDocument")]
    [Description("Ingests a document's content into the NetIndex pipeline: chunk, embed, and store.")]
    public async Task<NetIndexIngestionResult> IngestDocumentAsync(
        [Description("The identifier to assign to the ingested document.")] string documentId,
        [Description("The full text content of the document to ingest.")] string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var document = new PluginDocument(documentId, content);
        await _pipeline.IngestAsync(document, cancellationToken).ConfigureAwait(false);

        return new NetIndexIngestionResult(documentId);
    }

    [KernelFunction("GenerateAnswer")]
    [Description("Generates an answer to a query using retrieval-augmented generation over the NetIndex index.")]
    public async Task<string> GenerateAnswerAsync(
        [Description("The user's question or prompt.")] string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var answer = new StringBuilder();
        await foreach (var chunk in _pipeline.GenerateAsync(query, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            answer.Append(chunk.Text);
        }

        return answer.ToString();
    }
}
