using NetIndex.Core.Abstractions;
using NetIndex.Core.Abstractions.Telemetry;

namespace NetIndex.Testing.Common;

public sealed class TelemetryAndExceptionSmokeTests
{
    [Fact]
    public void Story14_ActivitySource_IsSingleton_WithExpectedName()
    {
        var first = NetIndexActivitySource.Source;
        var second = NetIndexActivitySource.Source;

        Assert.Same(first, second);
        Assert.Equal("NetIndex", first.Name);
    }

    [Fact]
    public void Story14_SpanNames_MatchSpec()
    {
        Assert.Equal("netindex.ingest", NetIndexSpanNames.Ingest);
        Assert.Equal("netindex.chunk", NetIndexSpanNames.Chunk);
        Assert.Equal("netindex.embed", NetIndexSpanNames.Embed);
        Assert.Equal("netindex.retrieve", NetIndexSpanNames.Retrieve);
        Assert.Equal("netindex.generate", NetIndexSpanNames.Generate);

        Assert.All(
            new[]
            {
                NetIndexSpanNames.Ingest,
                NetIndexSpanNames.Chunk,
                NetIndexSpanNames.Embed,
                NetIndexSpanNames.Retrieve,
                NetIndexSpanNames.Generate,
            },
            spanName => Assert.False(string.IsNullOrWhiteSpace(spanName)));
    }

    [Fact]
    public void Story14_ExceptionConstructors_PreserveInnerExceptionIdentity()
    {
        var inner = new InvalidOperationException("seed");

        Assert.Same(inner, new NetIndexConfigurationException("message", "Dimensions", 384, 768, inner).InnerException);
        Assert.Same(inner, new NetIndexAuthorizationException("message", "tenant", "claim", "failure", inner).InnerException);
        Assert.Same(inner, new NetIndexOcrNotInstalledException("message", "tesseract-ocr", "install", inner).InnerException);
        Assert.Same(inner, new NetIndexProviderException("message", true, "provider", "rate_limit", 429, inner).InnerException);
        Assert.Same(inner, new NetIndexStorageException("message", "store", "Upsert", "document", inner).InnerException);
    }

    [Fact]
    public void Story14_ExceptionConstructors_AllowNullInnerException()
    {
        Assert.Null(new NetIndexConfigurationException("message", "Dimensions", 384, 768, null).InnerException);
        Assert.Null(new NetIndexAuthorizationException("message", "tenant", "claim", "failure", null).InnerException);
        Assert.Null(new NetIndexOcrNotInstalledException("message", "tesseract-ocr", "install", null).InnerException);
        Assert.Null(new NetIndexProviderException("message", true, "provider", "rate_limit", 429, null).InnerException);
        Assert.Null(new NetIndexStorageException("message", "store", "Upsert", "document", null).InnerException);
    }
}