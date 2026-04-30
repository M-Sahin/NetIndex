using NetIndex.Core.Abstractions;

namespace NetIndex.Testing.Common;

public sealed class ResultTypesAndExceptionsSmokeTests
{
    [Fact]
    public void Story13_RecordTypes_SupportNondestructiveMutation()
    {
        var chunk = new RagChunk("chunk-1", "text", new[] { 1f, 0f }, "document-1", null);
        var updatedChunk = chunk with { Text = "updated" };

        var searchResult = new SearchResult<RagChunk>(chunk, 0.9f, "document-1");
        var updatedSearchResult = searchResult with { Score = 0.5f };

        var retrievalResult = new RetrievalResult("query", new[] { searchResult }, TimeSpan.FromMilliseconds(12));
        var updatedRetrievalResult = retrievalResult with { Query = "updated query" };

        var generationChunk = new GenerationChunk("partial", false, FinishReason.Stop);
        var updatedGenerationChunk = generationChunk with { IsComplete = true };

        var chunkingOptions = new ChunkingOptions(512, 64, "\n\n");
        var updatedChunkingOptions = chunkingOptions with { ChunkSize = 256 };

        Assert.Equal("text", chunk.Text);
        Assert.Equal("updated", updatedChunk.Text);
        Assert.Equal(0.9f, searchResult.Score);
        Assert.Equal(0.5f, updatedSearchResult.Score);
        Assert.Equal("query", retrievalResult.Query);
        Assert.Equal("updated query", updatedRetrievalResult.Query);
        Assert.False(generationChunk.IsComplete);
        Assert.True(updatedGenerationChunk.IsComplete);
        Assert.Equal(512, chunkingOptions.ChunkSize);
        Assert.Equal(256, updatedChunkingOptions.ChunkSize);
    }

    [Fact]
    public void Story13_ExceptionTypes_ExtendNetIndexException()
    {
        NetIndexException[] exceptions =
        {
            new NetIndexConfigurationException("config"),
            new NetIndexAuthorizationException("auth"),
            new NetIndexOcrNotInstalledException("ocr"),
            new NetIndexProviderException("provider"),
            new NetIndexStorageException("storage"),
        };

        Assert.All(exceptions, exception => Assert.IsAssignableFrom<NetIndexException>(exception));
    }

    [Fact]
    public void Story13_FinishReason_HasExpectedValues()
    {
        var values = Enum.GetValues<FinishReason>();

        Assert.Equal(
            new[]
            {
                FinishReason.Stop,
                FinishReason.Length,
                FinishReason.ContentFilter,
                FinishReason.Cancelled,
                FinishReason.Error,
            },
            values);
    }
}