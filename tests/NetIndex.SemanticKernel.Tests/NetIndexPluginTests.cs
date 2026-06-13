using FluentAssertions;
using Microsoft.SemanticKernel;
using NetIndex.Core.Abstractions;
using NSubstitute;
using Xunit;

namespace NetIndex.SemanticKernel.Tests;

/// <summary>
/// Tests for the NetIndex Semantic Kernel plugin's function metadata, invocation behavior, and error propagation.
/// </summary>
public class NetIndexPluginTests
{
    // ── AC-2: exact tool surface and semantic metadata ──

    /// <summary>
    /// Verifies that the plugin exposes exactly the RetrieveChunks, IngestDocument, and GenerateAnswer functions.
    /// </summary>
    [Fact]
    public void Plugin_ExposesExactlyThreeFunctions_WithExpectedNames()
    {
        var plugin = CreatePlugin(Substitute.For<INetIndexPipeline>());

        plugin.FunctionCount.Should().Be(3);
        plugin.Select(f => f.Name).Should().BeEquivalentTo("RetrieveChunks", "IngestDocument", "GenerateAnswer");
    }

    /// <summary>
    /// Verifies that each plugin function has a non-empty description.
    /// </summary>
    [Theory]
    [InlineData("RetrieveChunks")]
    [InlineData("IngestDocument")]
    [InlineData("GenerateAnswer")]
    public void Plugin_Function_HasNonEmptyDescription(string functionName)
    {
        var plugin = CreatePlugin(Substitute.For<INetIndexPipeline>());

        var metadata = plugin.GetFunctionsMetadata().Single(f => f.Name == functionName);

        metadata.Description.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies that the RetrieveChunks query parameter has a description and is required.
    /// </summary>
    [Fact]
    public void RetrieveChunks_QueryParameter_HasDescriptionAndIsRequired()
    {
        var plugin = CreatePlugin(Substitute.For<INetIndexPipeline>());
        var metadata = plugin.GetFunctionsMetadata().Single(f => f.Name == "RetrieveChunks");

        metadata.Parameters.Should().ContainSingle();
        var query = metadata.Parameters[0];
        query.Name.Should().Be("query");
        query.Description.Should().NotBeNullOrWhiteSpace();
        query.IsRequired.Should().BeTrue();
        query.ParameterType.Should().Be(typeof(string));
    }

    /// <summary>
    /// Verifies that the IngestDocument parameters have descriptions and are required.
    /// </summary>
    [Fact]
    public void IngestDocument_Parameters_HaveDescriptionsAndAreRequired()
    {
        var plugin = CreatePlugin(Substitute.For<INetIndexPipeline>());
        var metadata = plugin.GetFunctionsMetadata().Single(f => f.Name == "IngestDocument");

        metadata.Parameters.Select(p => p.Name).Should().BeEquivalentTo("documentId", "content");
        metadata.Parameters.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.Description) && p.IsRequired);
    }

    /// <summary>
    /// Verifies that the GenerateAnswer query parameter has a description and is required.
    /// </summary>
    [Fact]
    public void GenerateAnswer_QueryParameter_HasDescriptionAndIsRequired()
    {
        var plugin = CreatePlugin(Substitute.For<INetIndexPipeline>());
        var metadata = plugin.GetFunctionsMetadata().Single(f => f.Name == "GenerateAnswer");

        metadata.Parameters.Should().ContainSingle();
        var query = metadata.Parameters[0];
        query.Name.Should().Be("query");
        query.Description.Should().NotBeNullOrWhiteSpace();
        query.IsRequired.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that no plugin function exposes a cancellationToken parameter.
    /// </summary>
    [Fact]
    public void Plugin_FunctionParameters_DoNotIncludeCancellationToken()
    {
        var plugin = CreatePlugin(Substitute.For<INetIndexPipeline>());

        foreach (var metadata in plugin.GetFunctionsMetadata())
        {
            metadata.Parameters.Should().NotContain(p => p.Name.Equals("cancellationToken", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Verifies that the RetrieveChunks return schema documents score and metadata without exposing embeddings.
    /// </summary>
    [Fact]
    public void RetrieveChunks_ReturnSchema_DescribesScoreAndMetadataWithoutEmbeddings()
    {
        var plugin = CreatePlugin(Substitute.For<INetIndexPipeline>());
        var metadata = plugin.GetFunctionsMetadata().Single(f => f.Name == "RetrieveChunks");

        var schema = metadata.ReturnParameter.Schema!.RootElement.ToString();

        schema.Should().Contain("relevance score");
        schema.Should().Contain("\"Metadata\"");
        schema.Should().NotContain("Embedding");
    }

    /// <summary>
    /// Verifies that the RetrieveChunks return schema describes the chunk identifier, document identifier, and text properties.
    /// </summary>
    [Fact]
    public void RetrieveChunks_ReturnSchema_DescribesChunkIdDocumentIdAndText()
    {
        var plugin = CreatePlugin(Substitute.For<INetIndexPipeline>());
        var metadata = plugin.GetFunctionsMetadata().Single(f => f.Name == "RetrieveChunks");

        var schema = metadata.ReturnParameter.Schema!.RootElement.ToString();

        schema.Should().Contain("identifier of the retrieved chunk");
        schema.Should().Contain("identifier of the document the chunk belongs to");
        schema.Should().Contain("text content of the chunk");
    }

    /// <summary>
    /// Verifies that the IngestDocument return schema documents the document identifier.
    /// </summary>
    [Fact]
    public void IngestDocument_ReturnSchema_DescribesDocumentId()
    {
        var plugin = CreatePlugin(Substitute.For<INetIndexPipeline>());
        var metadata = plugin.GetFunctionsMetadata().Single(f => f.Name == "IngestDocument");

        var schema = metadata.ReturnParameter.Schema!.RootElement.ToString();

        schema.Should().Contain("\"DocumentId\"");
        schema.Should().Contain("\"description\"");
    }

    // ── AC-3: RetrieveChunks delegates to the pipeline ──

    /// <summary>
    /// Verifies that invoking RetrieveChunks calls QueryAsync once with the supplied query.
    /// </summary>
    [Fact]
    public async Task RetrieveChunks_Invoked_CallsQueryAsyncOnceWithQueryAsync()
    {
        var pipeline = Substitute.For<INetIndexPipeline>();
        pipeline.QueryAsync("hello", Arg.Any<CancellationToken>()).Returns(EmptySearchResultsAsync());
        var (kernel, plugin) = CreatePluginWithKernel(pipeline);

        await kernel.InvokeAsync(plugin["RetrieveChunks"], new KernelArguments { ["query"] = "hello" });

        pipeline.Received(1).QueryAsync("hello", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that RetrieveChunks projects query results preserving order, score, identifiers, text, and metadata.
    /// </summary>
    [Fact]
    public async Task RetrieveChunks_Invoked_ProjectsResultsPreservingOrderScoreIdsTextAndMetadataAsync()
    {
        var chunk1 = new RagChunk("c1", "text one", new float[] { 1, 2, 3 }, "doc-1",
            new Dictionary<string, string> { ["source"] = "a.txt" });
        var chunk2 = new RagChunk("c2", "text two", null, "doc-2", null);

        var pipeline = Substitute.For<INetIndexPipeline>();
        pipeline.QueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(StreamResultsAsync(
                new SearchResult<RagChunk>(chunk1, 0.9f, "doc-1"),
                new SearchResult<RagChunk>(chunk2, 0.4f, "doc-2")));

        var (kernel, plugin) = CreatePluginWithKernel(pipeline);

        var result = await kernel.InvokeAsync(plugin["RetrieveChunks"], new KernelArguments { ["query"] = "q" });
        var chunks = result.GetValue<IReadOnlyList<NetIndexRetrievedChunk>>()!;

        chunks.Should().HaveCount(2);
        chunks[0].Should().BeEquivalentTo(new
        {
            ChunkId = "c1",
            DocumentId = "doc-1",
            Text = "text one",
            Score = 0.9f,
            Metadata = new Dictionary<string, string> { ["source"] = "a.txt" }
        });
        chunks[1].Should().BeEquivalentTo(new
        {
            ChunkId = "c2",
            DocumentId = "doc-2",
            Text = "text two",
            Score = 0.4f,
            Metadata = new Dictionary<string, string>()
        });
    }

    /// <summary>
    /// Verifies that RetrieveChunks preserves the comparer of the source metadata dictionary.
    /// </summary>
    [Fact]
    public async Task RetrieveChunks_Invoked_PreservesSourceDictionaryComparerAsync()
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Source"] = "a.txt" };
        var chunk = new RagChunk("c1", "text", null, "doc-1", metadata);

        var pipeline = Substitute.For<INetIndexPipeline>();
        pipeline.QueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(StreamResultsAsync(new SearchResult<RagChunk>(chunk, 1.0f, "doc-1")));

        var (kernel, plugin) = CreatePluginWithKernel(pipeline);

        var result = await kernel.InvokeAsync(plugin["RetrieveChunks"], new KernelArguments { ["query"] = "q" });
        var chunks = result.GetValue<IReadOnlyList<NetIndexRetrievedChunk>>()!;

        var resultMetadata = (Dictionary<string, string>)chunks[0].Metadata;
        resultMetadata.Comparer.Should().Be(StringComparer.OrdinalIgnoreCase);
        resultMetadata["source"].Should().Be("a.txt");
    }

    /// <summary>
    /// Verifies that a blank query throws <see cref="ArgumentException"/> without calling the pipeline.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RetrieveChunks_BlankQuery_ThrowsArgumentExceptionWithoutCallingPipelineAsync(string query)
    {
        var pipeline = Substitute.For<INetIndexPipeline>();
        var (kernel, plugin) = CreatePluginWithKernel(pipeline);

        var act = () => kernel.InvokeAsync(plugin["RetrieveChunks"], new KernelArguments { ["query"] = query });

        await act.Should().ThrowAsync<ArgumentException>();
        pipeline.DidNotReceive().QueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that the cancellation token passed to RetrieveChunks is forwarded to QueryAsync.
    /// </summary>
    [Fact]
    public async Task RetrieveChunks_Invoked_PropagatesCancellationTokenToQueryAsync()
    {
        var capturedToken = CancellationToken.None;
        var pipeline = Substitute.For<INetIndexPipeline>();
        pipeline.QueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedToken = call.ArgAt<CancellationToken>(1);
                return EmptySearchResultsAsync();
            });

        var (kernel, plugin) = CreatePluginWithKernel(pipeline);
        using var cts = new CancellationTokenSource();

        await kernel.InvokeAsync(plugin["RetrieveChunks"], new KernelArguments { ["query"] = "q" }, cts.Token);

        capturedToken.Should().Be(cts.Token);
    }

    /// <summary>
    /// Verifies that the cancellation token passed to RetrieveChunks reaches the QueryAsync result enumerator itself.
    /// </summary>
    [Fact]
    public async Task RetrieveChunks_Invoked_PropagatesCancellationTokenToQueryAsyncEnumeratorAsync()
    {
        var results = new CancellationCapturingAsyncEnumerable<SearchResult<RagChunk>>();
        var pipeline = Substitute.For<INetIndexPipeline>();
        pipeline.QueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(results);

        var (kernel, plugin) = CreatePluginWithKernel(pipeline);
        using var cts = new CancellationTokenSource();

        await kernel.InvokeAsync(plugin["RetrieveChunks"], new KernelArguments { ["query"] = "q" }, cts.Token);

        results.CapturedToken.Should().Be(cts.Token);
    }

    // ── AC-4: IngestDocument delegates content ingestion ──

    /// <summary>
    /// Verifies that invoking IngestDocument calls IngestAsync once with a document preserving the supplied id and content.
    /// </summary>
    [Fact]
    public async Task IngestDocument_Invoked_CallsIngestAsyncOnceWithDocumentPreservingIdAndContentAsync()
    {
        IDocument? captured = null;
        var pipeline = Substitute.For<INetIndexPipeline>();
        pipeline.IngestAsync(Arg.Do<IDocument>(d => captured = d), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var (kernel, plugin) = CreatePluginWithKernel(pipeline);

        var result = await kernel.InvokeAsync(plugin["IngestDocument"], new KernelArguments
        {
            ["documentId"] = "doc-42",
            ["content"] = "the content"
        });

        await pipeline.Received(1).IngestAsync(Arg.Any<IDocument>(), Arg.Any<CancellationToken>());
        captured.Should().NotBeNull();
        captured!.Id.Should().Be("doc-42");
        captured.Content.Should().Be("the content");
        captured.Metadata.Should().BeNull();
        captured.SourceUri.Should().BeNull();

        var ingestion = result.GetValue<NetIndexIngestionResult>();
        ingestion!.DocumentId.Should().Be("doc-42");
    }

    /// <summary>
    /// Verifies that a blank document id or content throws <see cref="ArgumentException"/> without calling the pipeline.
    /// </summary>
    [Theory]
    [InlineData("", "content")]
    [InlineData("   ", "content")]
    [InlineData("doc-1", "")]
    [InlineData("doc-1", "   ")]
    public async Task IngestDocument_BlankIdOrContent_ThrowsArgumentExceptionWithoutCallingPipelineAsync(string documentId, string content)
    {
        var pipeline = Substitute.For<INetIndexPipeline>();
        var (kernel, plugin) = CreatePluginWithKernel(pipeline);

        var act = () => kernel.InvokeAsync(plugin["IngestDocument"], new KernelArguments
        {
            ["documentId"] = documentId,
            ["content"] = content
        });

        await act.Should().ThrowAsync<ArgumentException>();
        await pipeline.DidNotReceive().IngestAsync(Arg.Any<IDocument>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that the cancellation token passed to IngestDocument is forwarded to IngestAsync.
    /// </summary>
    [Fact]
    public async Task IngestDocument_Invoked_PropagatesCancellationTokenToIngestAsync()
    {
        var capturedToken = CancellationToken.None;
        var pipeline = Substitute.For<INetIndexPipeline>();
        pipeline.IngestAsync(Arg.Any<IDocument>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedToken = call.ArgAt<CancellationToken>(1);
                return Task.CompletedTask;
            });

        var (kernel, plugin) = CreatePluginWithKernel(pipeline);
        using var cts = new CancellationTokenSource();

        await kernel.InvokeAsync(plugin["IngestDocument"], new KernelArguments
        {
            ["documentId"] = "doc-1",
            ["content"] = "content"
        }, cts.Token);

        capturedToken.Should().Be(cts.Token);
    }

    // ── AC-5: GenerateAnswer adapts streaming generation ──

    /// <summary>
    /// Verifies that invoking GenerateAnswer calls GenerateAsync once and concatenates the streamed chunks in order.
    /// </summary>
    [Fact]
    public async Task GenerateAnswer_Invoked_CallsGenerateAsyncOnceAndConcatenatesChunksInOrderAsync()
    {
        var pipeline = Substitute.For<INetIndexPipeline>();
        pipeline.GenerateAsync("q", Arg.Any<CancellationToken>())
            .Returns(StreamGenerationAsync("Hello, ", "world", "!"));

        var (kernel, plugin) = CreatePluginWithKernel(pipeline);

        var result = await kernel.InvokeAsync(plugin["GenerateAnswer"], new KernelArguments { ["query"] = "q" });

        result.GetValue<string>().Should().Be("Hello, world!");
        pipeline.Received(1).GenerateAsync("q", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that GenerateAnswer returns an empty string when the pipeline yields no chunks.
    /// </summary>
    [Fact]
    public async Task GenerateAnswer_Invoked_EmptyStream_ReturnsEmptyStringAsync()
    {
        var pipeline = Substitute.For<INetIndexPipeline>();
        pipeline.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(StreamGenerationAsync());

        var (kernel, plugin) = CreatePluginWithKernel(pipeline);

        var result = await kernel.InvokeAsync(plugin["GenerateAnswer"], new KernelArguments { ["query"] = "q" });

        result.GetValue<string>().Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a blank query throws <see cref="ArgumentException"/> without calling the pipeline.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task GenerateAnswer_BlankQuery_ThrowsArgumentExceptionWithoutCallingPipelineAsync(string query)
    {
        var pipeline = Substitute.For<INetIndexPipeline>();
        var (kernel, plugin) = CreatePluginWithKernel(pipeline);

        var act = () => kernel.InvokeAsync(plugin["GenerateAnswer"], new KernelArguments { ["query"] = query });

        await act.Should().ThrowAsync<ArgumentException>();
        pipeline.DidNotReceive().GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that the cancellation token passed to GenerateAnswer is forwarded to GenerateAsync.
    /// </summary>
    [Fact]
    public async Task GenerateAnswer_Invoked_PropagatesCancellationTokenToGenerateAsync()
    {
        var capturedToken = CancellationToken.None;
        var pipeline = Substitute.For<INetIndexPipeline>();
        pipeline.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedToken = call.ArgAt<CancellationToken>(1);
                return StreamGenerationAsync("ok");
            });

        var (kernel, plugin) = CreatePluginWithKernel(pipeline);
        using var cts = new CancellationTokenSource();

        await kernel.InvokeAsync(plugin["GenerateAnswer"], new KernelArguments { ["query"] = "q" }, cts.Token);

        capturedToken.Should().Be(cts.Token);
    }

    /// <summary>
    /// Verifies that the cancellation token passed to GenerateAnswer reaches the GenerateAsync result enumerator itself.
    /// </summary>
    [Fact]
    public async Task GenerateAnswer_Invoked_PropagatesCancellationTokenToGenerateAsyncEnumeratorAsync()
    {
        var chunks = new CancellationCapturingAsyncEnumerable<GenerationChunk>();
        var pipeline = Substitute.For<INetIndexPipeline>();
        pipeline.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(chunks);

        var (kernel, plugin) = CreatePluginWithKernel(pipeline);
        using var cts = new CancellationTokenSource();

        await kernel.InvokeAsync(plugin["GenerateAnswer"], new KernelArguments { ["query"] = "q" }, cts.Token);

        chunks.CapturedToken.Should().Be(cts.Token);
    }

    // ── AC-6 / AC-8: authorization and error contracts remain owned by NetIndex ──

    /// <summary>
    /// Verifies that an authorization exception thrown by the pipeline during RetrieveChunks propagates as the root cause.
    /// </summary>
    [Fact]
    [Trait("Category", "SecurityContract")]
    public async Task RetrieveChunks_Invoked_PipelineAuthorizationException_PropagatesAsRootCauseAsync()
    {
        var authException = new NetIndexAuthorizationException("denied", "tenant-1", "tenant_id", "NoTenantResolverConfigured");
        var pipeline = Substitute.For<INetIndexPipeline>();
        pipeline.QueryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ThrowingSearchResultsAsync(authException));

        var (kernel, plugin) = CreatePluginWithKernel(pipeline);

        var act = () => kernel.InvokeAsync(plugin["RetrieveChunks"], new KernelArguments { ["query"] = "q" });

        var thrown = await act.Should().ThrowAsync<Exception>();
        var rootCause = UnwrapRootCause(thrown.Which);
        rootCause.Should().BeOfType<NetIndexAuthorizationException>();
        ((NetIndexAuthorizationException)rootCause).FailureReason.Should().Be("NoTenantResolverConfigured");
    }

    /// <summary>
    /// Verifies that a provider exception thrown by the pipeline during GenerateAnswer propagates as the root cause.
    /// </summary>
    [Fact]
    public async Task GenerateAnswer_Invoked_PipelineProviderException_PropagatesAsRootCauseAsync()
    {
        var providerException = new NetIndexProviderException("boom");
        var pipeline = Substitute.For<INetIndexPipeline>();
        pipeline.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ThrowingGenerationAsync(providerException));

        var (kernel, plugin) = CreatePluginWithKernel(pipeline);

        var act = () => kernel.InvokeAsync(plugin["GenerateAnswer"], new KernelArguments { ["query"] = "q" });

        var thrown = await act.Should().ThrowAsync<Exception>();
        UnwrapRootCause(thrown.Which).Should().BeOfType<NetIndexProviderException>();
    }

    /// <summary>
    /// Verifies that an authorization exception thrown by the pipeline during IngestDocument propagates as the root cause.
    /// </summary>
    [Fact]
    [Trait("Category", "SecurityContract")]
    public async Task IngestDocument_Invoked_PipelineAuthorizationException_PropagatesAsRootCauseAsync()
    {
        var authException = new NetIndexAuthorizationException("denied");
        var pipeline = Substitute.For<INetIndexPipeline>();
        pipeline.IngestAsync(Arg.Any<IDocument>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(authException));

        var (kernel, plugin) = CreatePluginWithKernel(pipeline);

        var act = () => kernel.InvokeAsync(plugin["IngestDocument"], new KernelArguments
        {
            ["documentId"] = "doc-1",
            ["content"] = "content"
        });

        var thrown = await act.Should().ThrowAsync<Exception>();
        UnwrapRootCause(thrown.Which).Should().BeOfType<NetIndexAuthorizationException>();
    }

    // ── Helpers ──

    private static KernelPlugin CreatePlugin(INetIndexPipeline pipeline)
        => new KernelPluginCollection().AddNetIndexPlugin(pipeline);

    private static (Kernel Kernel, KernelPlugin Plugin) CreatePluginWithKernel(INetIndexPipeline pipeline)
    {
        var kernel = new Kernel();
        var plugin = kernel.Plugins.AddNetIndexPlugin(pipeline);
        return (kernel, plugin);
    }

    private static async IAsyncEnumerable<SearchResult<RagChunk>> EmptySearchResultsAsync()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<SearchResult<RagChunk>> StreamResultsAsync(params SearchResult<RagChunk>[] results)
    {
        foreach (var result in results)
        {
            yield return result;
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<SearchResult<RagChunk>> ThrowingSearchResultsAsync(Exception exception)
    {
        await Task.Yield();
        throw exception;
#pragma warning disable CS0162 // Unreachable code detected
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<GenerationChunk> StreamGenerationAsync(params string[] texts)
    {
        for (var i = 0; i < texts.Length; i++)
        {
            yield return new GenerationChunk(texts[i], i == texts.Length - 1, FinishReason.Stop);
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<GenerationChunk> ThrowingGenerationAsync(Exception exception)
    {
        await Task.Yield();
        throw exception;
#pragma warning disable CS0162 // Unreachable code detected
        yield break;
#pragma warning restore CS0162
    }

    private static Exception UnwrapRootCause(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current;
    }

    /// <summary>
    /// An empty <see cref="IAsyncEnumerable{T}"/> that records the <see cref="CancellationToken"/> passed to
    /// <see cref="GetAsyncEnumerator"/>, so tests can prove cancellation reaches the enumerator itself
    /// (not just the pipeline method call).
    /// </summary>
    private sealed class CancellationCapturingAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        public CancellationToken CapturedToken { get; private set; }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            CapturedToken = cancellationToken;
            return Empty();
        }

        private static async IAsyncEnumerator<T> Empty()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
