using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.TextEmbeddingsInference.Options;
using Xunit;

namespace NetIndex.Providers.TextEmbeddingsInference.Tests;

public class TeiHardeningTests
{
    [Fact]
    public async Task Redirect_IsNotFollowedAndSurfacesAsNonRetryableHttp307Async()
    {
        var handler = new FakeHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            response.Headers.Location = new Uri("http://elsewhere.example/rerank");
            return Task.FromResult(response);
        });
        using var sut = TeiFactory.Create(handler);

        var act = () => sut.RerankAsync(TeiFactory.Results(2), "q");

        var ex = (await act.Should().ThrowAsync<NetIndexProviderException>()).Which;
        ex.ErrorCode.Should().Be("http_307");
        ex.IsRetryable.Should().BeFalse();
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public void Handler_NeverFollowsRedirects()
    {
        using var handler = TeiDocumentReranker.CreateHandler(new TeiRerankerOptions());

        handler.AllowAutoRedirect.Should().BeFalse();
        handler.ConnectCallback.Should().BeNull("the private-network guard is opt-in");
    }

    [Fact]
    public void Handler_InstallsTheConnectGuardOnlyWhenRestricted()
    {
        using var handler = TeiDocumentReranker.CreateHandler(new TeiRerankerOptions { RestrictToPrivateNetwork = true });

        handler.ConnectCallback.Should().NotBeNull();
    }

    [Theory]
    [InlineData("http://reranker/?a=b")]
    [InlineData("http://reranker/#frag")]
    [InlineData("http://reranker?")]
    public void Validator_RejectsEndpointWithQueryOrFragment(string endpoint)
    {
        new TeiRerankerOptionsValidator().Validate(null, new TeiRerankerOptions { Endpoint = endpoint }).Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData("http://reranker", "http://reranker/rerank")]
    [InlineData("http://reranker/", "http://reranker/rerank")]
    [InlineData("http://reranker:8080/base", "http://reranker:8080/base/rerank")]
    public async Task Endpoint_IsJoinedWithTheRerankRouteAsync(string endpoint, string expected)
    {
        var handler = FakeHandler.Json("[{\"index\":0,\"score\":0.1},{\"index\":1,\"score\":0.2}]");
        using var sut = new TeiDocumentReranker(new HttpClient(handler), new TeiRerankerOptions { Endpoint = endpoint }, TimeProvider.System);

        await sut.RerankAsync(TeiFactory.Results(2), "q");

        handler.LastUri!.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("169.254.1.1", true)]
    [InlineData("::1", true)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("::ffff:10.0.0.1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("172.15.0.1", false)]
    [InlineData("192.169.0.1", false)]
    [InlineData("11.0.0.1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("::ffff:8.8.8.8", false)]
    public void PrivateAddressPredicate_AllowsOnlyPrivateRanges(string address, bool expected)
    {
        TeiPrivateNetworkGuard.IsPrivateAddress(IPAddress.Parse(address)).Should().Be(expected);
    }

    [Fact]
    public async Task Guard_ConnectsToALoopbackListenerAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = listener.AcceptTcpClientAsync();

        await using var stream = await TeiPrivateNetworkGuard.ConnectAsync(
            new DnsEndPoint("localhost-test", port), (_, _) => Task.FromResult(new[] { IPAddress.Loopback }), CancellationToken.None);

        stream.Should().NotBeNull();
        using var client = await accepted;
        client.Connected.Should().BeTrue();
    }

    [Fact]
    public Task Guard_RefusesAPublicAnswerWithoutConnectingAsync()
    {
        var act = () => TeiPrivateNetworkGuard.ConnectAsync(
            new DnsEndPoint("reranker", 80),
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") }),
            CancellationToken.None).AsTask();

        return act.Should().ThrowAsync<TeiNonPrivateAddressException>();
    }

    [Fact]
    public Task Guard_RefusesAMixedAnswerAsync()
    {
        var act = () => TeiPrivateNetworkGuard.ConnectAsync(
            new DnsEndPoint("reranker", 80),
            (_, _) => Task.FromResult(new[] { IPAddress.Loopback, IPAddress.Parse("8.8.8.8") }),
            CancellationToken.None).AsTask();

        return act.Should().ThrowAsync<TeiNonPrivateAddressException>();
    }

    [Fact]
    public async Task RestrictedReranker_RefusesAPublicAddressAsRetryableNonPrivateAddressAsync()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new TeiRerankerOptions { Endpoint = "http://8.8.8.8:81", RestrictToPrivateNetwork = true });
        using var sut = new TeiDocumentReranker(options);

        var act = () => sut.RerankAsync(TeiFactory.Results(2), "q");

        var ex = (await act.Should().ThrowAsync<NetIndexProviderException>()).Which;
        ex.ErrorCode.Should().Be("non_private_address");
        ex.IsRetryable.Should().BeTrue();
        ex.Should().NotBeAssignableTo<HttpRequestException>();
    }
}
