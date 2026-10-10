using FluentAssertions;
using NetIndex.Core.Abstractions;
using Xunit;

namespace NetIndex.Providers.TextEmbeddingsInference.Tests;

public class TeiDocumentRerankerTests
{
    [Fact]
    public async Task RerankAsync_OrdersByScoreDescendingAndMapsByIndexAsync()
    {
        var handler = FakeHandler.Json("[{\"index\":2,\"score\":0.9},{\"index\":0,\"score\":0.1},{\"index\":1,\"score\":0.5}]");
        using var sut = TeiFactory.Create(handler);

        var ranked = (await sut.RerankAsync(TeiFactory.Results(3), "q")).ToList();

        ranked.Select(r => r.Item.Id).Should().Equal("c2", "c1", "c0");
        ranked.Select(r => r.Score).Should().Equal(0.9f, 0.5f, 0.1f);
        ranked[0].DocumentId.Should().Be("doc-c2");
        ranked[0].Item.Text.Should().Be("text 2");
    }

    [Fact]
    public async Task RerankAsync_TiesKeepInputOrderAsync()
    {
        var handler = FakeHandler.Json("[{\"index\":2,\"score\":0.5},{\"index\":1,\"score\":0.5},{\"index\":0,\"score\":0.5}]");
        using var sut = TeiFactory.Create(handler);

        var ranked = await sut.RerankAsync(TeiFactory.Results(3), "q");

        ranked.Select(r => r.Item.Id).Should().Equal("c0", "c1", "c2");
    }

    [Fact]
    public async Task RerankAsync_SendsQueryTextsAndTruncateToRerankRouteAsync()
    {
        var handler = FakeHandler.Json("[{\"index\":0,\"score\":0.2},{\"index\":1,\"score\":0.3}]");
        using var sut = TeiFactory.Create(handler);

        await sut.RerankAsync(TeiFactory.Results(2), "wat is dit");

        handler.LastUri!.ToString().Should().Be("http://reranker/rerank");
        handler.LastBody.Should().Contain("\"query\":\"wat is dit\"")
            .And.Contain("\"texts\":[\"text 0\",\"text 1\"]")
            .And.Contain("\"truncate\":true");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task RerankAsync_ZeroOrOneResult_ReturnsInputWithoutCallAsync(int count)
    {
        var handler = FakeHandler.Throw(new InvalidOperationException("must not be called"));
        using var sut = TeiFactory.Create(handler);
        var input = TeiFactory.Results(count);

        var ranked = await sut.RerankAsync(input, "q");

        ranked.Should().Equal(input);
        handler.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"index\":0,\"score\":0.1},{\"index\":0,\"score\":0.2}]")]
    [InlineData("[{\"index\":0,\"score\":0.1},{\"index\":5,\"score\":0.2}]")]
    [InlineData("not json")]
    [InlineData("null")]
    public async Task RerankAsync_UnusableResponse_ThrowsNonRetryableInvalidResponseAsync(string json)
    {
        using var sut = TeiFactory.Create(FakeHandler.Json(json));

        var act = () => sut.RerankAsync(TeiFactory.Results(2), "q");

        var ex = (await act.Should().ThrowAsync<NetIndexProviderException>()).Which;
        ex.ErrorCode.Should().Be("invalid_response");
        ex.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public async Task RerankAsync_ConnectionRefused_ThrowsRetryableWithoutLeakingHttpRequestExceptionAsync()
    {
        using var sut = TeiFactory.Create(FakeHandler.Throw(new HttpRequestException("refused")));

        var act = () => sut.RerankAsync(TeiFactory.Results(2), "q");

        var ex = (await act.Should().ThrowAsync<NetIndexProviderException>()).Which;
        ex.IsRetryable.Should().BeTrue();
        ex.ErrorCode.Should().Be("connection_failed");
        ex.ProviderName.Should().Be("TextEmbeddingsInference");
        ex.Should().NotBeAssignableTo<HttpRequestException>();
    }

    [Fact]
    public async Task RerankAsync_RequestTimeout_ThrowsRetryableTimeoutAsync()
    {
        var handler = FakeHandler.Throw(new TaskCanceledException("timeout"));
        using var sut = TeiFactory.Create(handler);

        var act = () => sut.RerankAsync(TeiFactory.Results(2), "q");

        var ex = (await act.Should().ThrowAsync<NetIndexProviderException>()).Which;
        ex.IsRetryable.Should().BeTrue();
        ex.ErrorCode.Should().Be("timeout");
    }

    [Fact]
    public async Task RerankAsync_RealRequestTimeout_ThrowsRetryableTimeoutAsync()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(50) };
        using var sut = new TeiDocumentReranker(client, TeiFactory.Options(), TimeProvider.System);

        var act = () => sut.RerankAsync(TeiFactory.Results(2), "q");

        (await act.Should().ThrowAsync<NetIndexProviderException>()).Which.ErrorCode.Should().Be("timeout");
    }

    [Theory]
    [InlineData(500, true, "http_500")]
    [InlineData(503, true, "http_503")]
    [InlineData(429, true, "rate_limited")]
    [InlineData(408, true, "http_408")]
    [InlineData(400, false, "http_400")]
    [InlineData(413, false, "http_413")]
    [InlineData(422, false, "http_422")]
    public async Task RerankAsync_ErrorStatus_IsClassifiedAsync(int status, bool retryable, string code)
    {
        using var sut = TeiFactory.Create(FakeHandler.Status((System.Net.HttpStatusCode)status));

        var act = () => sut.RerankAsync(TeiFactory.Results(2), "q");

        var ex = (await act.Should().ThrowAsync<NetIndexProviderException>()).Which;
        ex.IsRetryable.Should().Be(retryable);
        ex.ErrorCode.Should().Be(code);
        ex.HttpStatusCode.Should().Be(status);
    }

    [Fact]
    public async Task RerankAsync_CallerCancellation_PropagatesAndIsNotWrappedAsync()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });
        using var sut = TeiFactory.Create(handler);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        var act = () => sut.RerankAsync(TeiFactory.Results(2), "q", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        sut.Breaker.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public Task RerankAsync_AfterDispose_ThrowsObjectDisposedAsync()
    {
        var sut = TeiFactory.Create(FakeHandler.Json("[]"));
        sut.Dispose();

        var act = () => sut.RerankAsync(TeiFactory.Results(2), "q");

        return act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
