using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using NetIndex.Core.Abstractions;
using Xunit;

namespace NetIndex.Providers.TextEmbeddingsInference.Tests.Contract;

/// <summary>Contract tests: every upstream failure is wrapped as a <see cref="NetIndexProviderException"/> (NFR9).</summary>
public class TeiProviderExceptionContractTests
{
    [Theory]
    [Trait("Category", "SecurityContract")]
    [InlineData(500, true)]
    [InlineData(502, true)]
    [InlineData(503, true)]
    [InlineData(429, true)]
    [InlineData(400, false)]
    [InlineData(404, false)]
    [InlineData(422, false)]
    public async Task WrapProviderException_OnErrorStatus_ClassifiesRetryabilityAsync(int status, bool retryable)
    {
        using var sut = TeiFactory.Create(FakeHandler.Status((HttpStatusCode)status));

        var act = () => sut.RerankAsync(TeiFactory.Results(2), "q");

        var ex = (await act.Should().ThrowAsync<NetIndexProviderException>()).Which;
        ex.IsRetryable.Should().Be(retryable);
        ex.ProviderName.Should().Be("TextEmbeddingsInference");
        ex.HttpStatusCode.Should().Be(status);
    }

    [Theory]
    [Trait("Category", "SecurityContract")]
    [MemberData(nameof(TransportFailures))]
    public async Task WrapProviderException_OnTransportFailure_NeverLeaksRawExceptionAsync(Exception raw)
    {
        using var sut = TeiFactory.Create(FakeHandler.Throw(raw));

        var act = () => sut.RerankAsync(TeiFactory.Results(2), "q");

        var ex = (await act.Should().ThrowAsync<NetIndexProviderException>()).Which;
        ex.IsRetryable.Should().BeTrue();
        ex.Should().NotBeAssignableTo<HttpRequestException>();
        ex.InnerException.Should().BeSameAs(raw);
    }

    public static IEnumerable<object[]> TransportFailures()
    {
        yield return new object[] { new HttpRequestException("refused") };
        yield return new object[] { new SocketException((int)SocketError.ConnectionRefused) };
        yield return new object[] { new IOException("reset") };
        yield return new object[] { new TaskCanceledException("connect timeout") };
    }
}
