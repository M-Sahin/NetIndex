using FluentAssertions;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Storage.Pgvector.Options;
using Xunit;

namespace NetIndex.Storage.Pgvector.Tests;

/// <summary>Lifecycle and disposal tests for <see cref="PgvectorVectorStore"/>.</summary>
public class PgvectorVectorStoreLifecycleTests
{
    private static PgvectorVectorStore CreateStore(string connectionString = "Host=localhost;Database=rag")
    {
        var options = new PgvectorOptions
        {
            ConnectionString = connectionString,
            Dimensions = 4,
        };
        return new PgvectorVectorStore(new OptionsWrapper<PgvectorOptions>(options));
    }

    /// <summary>DisposeAsync is idempotent: calling it twice does not throw.</summary>
    [Fact]
    public async Task DisposeAsync_Idempotent_CalledTwiceAsync()
    {
        var store = CreateStore();
        await store.DisposeAsync();

        var act = async () => await store.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    /// <summary>All public methods throw ObjectDisposedException after the store is disposed.</summary>
    [Theory]
    [InlineData("UpsertAsync")]
    [InlineData("QueryAsync")]
    [InlineData("DeleteAsync")]
    public async Task PublicMethods_AfterDispose_ThrowObjectDisposedExceptionAsync(string methodName)
    {
        var store = CreateStore();
        await store.DisposeAsync();

        Func<Task> act = methodName switch
        {
            "UpsertAsync" => () => store.UpsertAsync(Array.Empty<RagChunk>()),
            "QueryAsync" => async () =>
            {
                await foreach (var _ in store.QueryAsync(new float[4]))
                {
                    // drain
                }
            },
            "DeleteAsync" => () => store.DeleteAsync("doc-1"),
            _ => throw new ArgumentException($"Unknown method: {methodName}"),
        };

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    /// <summary>Constructor throws ArgumentNullException when options is null.</summary>
    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var act = () => new PgvectorVectorStore(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    /// <summary>Constructor throws ArgumentOutOfRangeException when Dimensions is zero or negative.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveDimensions_ThrowsArgumentOutOfRangeException(int dimensions)
    {
        var options = new PgvectorOptions
        {
            ConnectionString = "Host=localhost;Database=rag",
            Dimensions = dimensions,
        };
        var act = () => new PgvectorVectorStore(new OptionsWrapper<PgvectorOptions>(options));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
