using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Sockets;
using FluentAssertions;
using NetIndex.Core.Abstractions;
using NSubstitute;
using Xunit;

namespace NetIndex.Providers.OpenAI.Tests.Contract;

public sealed class OpenAIProviderExceptionContractTests
{
    // Create a ClientResultException with a specific HTTP status by backing it with a mocked PipelineResponse.
    private static ClientResultException CreateCre(int status, string message = "upstream failed")
    {
        if (status == 0)
        {
            return new ClientResultException(message);
        }
        var response = Substitute.For<PipelineResponse>();
        response.Status.Returns(status);
        return new ClientResultException(message, response);
    }

    [Theory]
    [Trait("Category", "SecurityContract")]
    [InlineData(408, "http_408", true, typeof(NetIndexProviderException))]
    [InlineData(429, "rate_limited", true, typeof(NetIndexProviderException))]
    [InlineData(500, "http_500", true, typeof(NetIndexProviderException))]
    [InlineData(502, "http_502", true, typeof(NetIndexProviderException))]
    [InlineData(503, "http_503", true, typeof(NetIndexProviderException))]
    [InlineData(504, "http_504", true, typeof(NetIndexProviderException))]
    [InlineData(501, "http_501", false, typeof(NetIndexProviderException))]
    [InlineData(401, "auth_failed", false, typeof(OpenAIAuthenticationException))]
    [InlineData(403, "auth_failed", false, typeof(OpenAIAuthenticationException))]
    [InlineData(400, "http_400", false, typeof(NetIndexProviderException))]
    [InlineData(404, "http_404", false, typeof(NetIndexProviderException))]
    [InlineData(422, "http_422", false, typeof(NetIndexProviderException))]
    public void WrapProviderException_OnClientResultExceptionWithStatus_ClassifiesCorrectly(
        int status,
        string errorCode,
        bool retryable,
        Type expectedType)
    {
        var upstream = CreateCre(status);

        var wrapped = OpenAIProviderHelpers.Wrap(upstream, CancellationToken.None);

        wrapped.Should().BeAssignableTo<NetIndexProviderException>();
        wrapped.Should().BeOfType(expectedType);
        var provider = (NetIndexProviderException)wrapped;
        provider.ErrorCode.Should().Be(errorCode);
        provider.IsRetryable.Should().Be(retryable);
        provider.ProviderName.Should().Be("OpenAI");
    }

    [Fact]
    [Trait("Category", "SecurityContract")]
    public void WrapProviderException_OnStatuslessClientResultException_MapsToClientResult()
    {
        // A statusless ClientResultException (Status==0) must be non-retryable client_result,
        // not misclassified as an HTTP error code.
        var upstream = CreateCre(0, "sdk protocol error");

        var wrapped = OpenAIProviderHelpers.Wrap(upstream, CancellationToken.None);

        wrapped.Should().BeOfType<NetIndexProviderException>();
        var provider = (NetIndexProviderException)wrapped;
        provider.ErrorCode.Should().Be("client_result");
        provider.IsRetryable.Should().BeFalse();
        provider.InnerException.Should().BeSameAs(upstream);
    }

    [Fact]
    [Trait("Category", "SecurityContract")]
    public void WrapProviderException_On429_IsRetryable_NotClientResult()
    {
        // Regression guard: 429 must NOT map to non-retryable client_result.
        // The standard OpenAI SDK surfaces rate-limit via ClientResultException.Status==429.
        var upstream = CreateCre(429);

        var wrapped = OpenAIProviderHelpers.Wrap(upstream, CancellationToken.None);

        var provider = (NetIndexProviderException)wrapped;
        provider.ErrorCode.Should().Be("rate_limited");
        provider.IsRetryable.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "SecurityContract")]
    public void WrapProviderException_On5xx_IsRetryable_NotClientResult()
    {
        // Regression guard: 5xx must NOT map to non-retryable client_result.
        var upstream = CreateCre(500);

        var wrapped = OpenAIProviderHelpers.Wrap(upstream, CancellationToken.None);

        var provider = (NetIndexProviderException)wrapped;
        provider.ErrorCode.Should().Be("http_500");
        provider.IsRetryable.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "SecurityContract")]
    public void WrapProviderException_On401_IsAuthException_NotClientResult()
    {
        // Regression guard: 401 must surface as OpenAIAuthenticationException, not client_result.
        var upstream = CreateCre(401);

        var wrapped = OpenAIProviderHelpers.Wrap(upstream, CancellationToken.None);

        wrapped.Should().BeOfType<OpenAIAuthenticationException>();
        ((NetIndexProviderException)wrapped).IsRetryable.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "SecurityContract")]
    public void WrapProviderException_OnCallerCancellation_RethrowsOriginal()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var upstream = new OperationCanceledException(cts.Token);

        var wrapped = OpenAIProviderHelpers.Wrap(upstream, cts.Token);

        wrapped.Should().BeSameAs(upstream);
    }

    [Fact]
    [Trait("Category", "SecurityContract")]
    public void WrapProviderException_OnCallerCancellation_ViaLinkedCts_RethrowsOriginal()
    {
        // The SDK may use an internal linked CTS; the OCE's token won't equal the caller's
        // token. Classification must check callerToken.IsCancellationRequested, not token equality.
        using var callerCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerCts.Token);
        callerCts.Cancel();
        var upstream = new OperationCanceledException(linkedCts.Token);

        var wrapped = OpenAIProviderHelpers.Wrap(upstream, callerCts.Token);

        wrapped.Should().BeSameAs(upstream);
    }

    [Fact]
    [Trait("Category", "SecurityContract")]
    public void WrapProviderException_OnUnknownException_WrapsAsProviderError()
    {
        var upstream = new InvalidOperationException("something unexpected from sdk");

        var wrapped = OpenAIProviderHelpers.Wrap(upstream, CancellationToken.None);

        var provider = wrapped.Should().BeOfType<NetIndexProviderException>().Subject;
        provider.ErrorCode.Should().Be("provider_error");
        provider.IsRetryable.Should().BeFalse();
        provider.InnerException.Should().BeSameAs(upstream);
        provider.ProviderName.Should().Be("OpenAI");
    }

    [Fact]
    [Trait("Category", "SecurityContract")]
    public void WrapProviderException_OnExistingNetIndexProviderException_PassesThrough()
    {
        // Exceptions already classified inside a try block (e.g., dimension checks) must not be
        // double-wrapped when the outer catch routes everything through Wrap.
        var upstream = new NetIndexProviderException(
            "already classified", isRetryable: false, providerName: "OpenAI",
            errorCode: "dimension_mismatch", httpStatusCode: null, innerException: null);

        var wrapped = OpenAIProviderHelpers.Wrap(upstream, CancellationToken.None);

        wrapped.Should().BeSameAs(upstream);
    }

    [Fact]
    [Trait("Category", "SecurityContract")]
    public void WrapProviderException_OnSdkTimeout_WrapsAsRetryableTimeout()
    {
        using var unusedCts = new CancellationTokenSource();
        var upstream = new OperationCanceledException("sdk timeout", unusedCts.Token);

        var wrapped = OpenAIProviderHelpers.Wrap(upstream, CancellationToken.None);

        wrapped.Should().BeOfType<NetIndexProviderException>();
        var provider = (NetIndexProviderException)wrapped;
        provider.ErrorCode.Should().Be("timeout");
        provider.IsRetryable.Should().BeTrue();
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

        var wrapped = OpenAIProviderHelpers.Wrap(upstream, CancellationToken.None);

        var provider = wrapped.Should().BeOfType<NetIndexProviderException>().Subject;
        provider.ErrorCode.Should().Be("network");
        provider.IsRetryable.Should().BeTrue();
        provider.ProviderName.Should().Be("OpenAI");
    }
}
