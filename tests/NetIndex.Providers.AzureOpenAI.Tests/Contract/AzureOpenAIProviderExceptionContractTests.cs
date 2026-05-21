using System.ClientModel;
using System.Net.Sockets;
using Azure;
using Azure.Identity;
using FluentAssertions;
using NetIndex.Core.Abstractions;
using Xunit;

namespace NetIndex.Providers.AzureOpenAI.Tests.Contract;

public sealed class AzureOpenAIProviderExceptionContractTests
{
    [Theory]
    [Trait("Category", "SecurityContract")]
    [InlineData(408, "http_408", true, typeof(NetIndexProviderException))]
    [InlineData(429, "rate_limited", true, typeof(NetIndexProviderException))]
    [InlineData(500, "http_500", true, typeof(NetIndexProviderException))]
    [InlineData(502, "http_502", true, typeof(NetIndexProviderException))]
    [InlineData(503, "http_503", true, typeof(NetIndexProviderException))]
    [InlineData(504, "http_504", true, typeof(NetIndexProviderException))]
    [InlineData(501, "http_501", false, typeof(NetIndexProviderException))]
    [InlineData(401, "auth_failed", false, typeof(AzureOpenAIAuthenticationException))]
    [InlineData(403, "auth_failed", false, typeof(AzureOpenAIAuthenticationException))]
    [InlineData(400, "http_400", false, typeof(NetIndexProviderException))]
    [InlineData(404, "http_404", false, typeof(NetIndexProviderException))]
    [InlineData(422, "http_422", false, typeof(NetIndexProviderException))]
    public void WrapProviderException_OnUpstreamFailure_ThrowsNetIndexProviderException(
        int status,
        string errorCode,
        bool retryable,
        Type expectedType)
    {
        var upstream = new RequestFailedException(status, "upstream failed");

        var wrapped = AzureOpenAIProviderHelpers.Wrap(upstream, CancellationToken.None);

        wrapped.Should().BeAssignableTo<NetIndexProviderException>();
        wrapped.Should().BeOfType(expectedType);
        var provider = (NetIndexProviderException)wrapped;
        provider.ErrorCode.Should().Be(errorCode);
        provider.IsRetryable.Should().Be(retryable);
        provider.ProviderName.Should().Be("AzureOpenAI");
    }

    [Fact]
    [Trait("Category", "SecurityContract")]
    public void WrapProviderException_OnAuthenticationFailedException_ThrowsAuthenticationException()
    {
        var wrapped = AzureOpenAIProviderHelpers.Wrap(new AuthenticationFailedException("bad credential"), CancellationToken.None);

        wrapped.Should().BeOfType<AzureOpenAIAuthenticationException>();
        ((NetIndexProviderException)wrapped).ErrorCode.Should().Be("credential_failed");
    }

    [Theory]
    [Trait("Category", "SecurityContract")]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(SocketException))]
    public void WrapProviderException_OnNetworkFailure_ThrowsRetryableProviderException(Type exceptionType)
    {
        Exception upstream = exceptionType == typeof(HttpRequestException)
            ? new HttpRequestException("network")
            : exceptionType == typeof(IOException)
                ? new IOException("io")
                : new SocketException((int)SocketError.ConnectionRefused);

        var wrapped = AzureOpenAIProviderHelpers.Wrap(upstream, CancellationToken.None);

        var provider = wrapped.Should().BeOfType<NetIndexProviderException>().Subject;
        provider.ErrorCode.Should().Be("network");
        provider.IsRetryable.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "SecurityContract")]
    public void WrapProviderException_OnClientResultException_WrapsAsProviderException()
    {
        // Patch #10: ClientResultException must not escape the provider boundary.
        var upstream = new ClientResultException("sdk client error");
        var wrapped = AzureOpenAIProviderHelpers.Wrap(upstream, CancellationToken.None);

        wrapped.Should().BeOfType<NetIndexProviderException>();
        var provider = (NetIndexProviderException)wrapped;
        provider.ErrorCode.Should().Be("client_result");
        provider.IsRetryable.Should().BeFalse();
        provider.InnerException.Should().BeSameAs(upstream);
    }

    [Fact]
    [Trait("Category", "SecurityContract")]
    public void WrapProviderException_OnCallerCancellation_RethrowsOriginal()
    {
        // Patch #10: caller-initiated cancellation should not be wrapped.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var upstream = new OperationCanceledException(cts.Token);

        var wrapped = AzureOpenAIProviderHelpers.Wrap(upstream, cts.Token);

        wrapped.Should().BeSameAs(upstream);
    }

    [Fact]
    [Trait("Category", "SecurityContract")]
    public void WrapProviderException_OnSDKTimeout_WrapsAsRetryableTimeout()
    {
        // Patch #10: non-caller CancellationToken (SDK internal timeout) should be wrapped.
        using var unusedTokenSource = new CancellationTokenSource();
        var upstream = new OperationCanceledException("sdk timeout", unusedTokenSource.Token);

        var wrapped = AzureOpenAIProviderHelpers.Wrap(upstream, CancellationToken.None);

        wrapped.Should().BeOfType<NetIndexProviderException>();
        var provider = (NetIndexProviderException)wrapped;
        provider.ErrorCode.Should().Be("timeout");
        provider.IsRetryable.Should().BeTrue();
    }
}
