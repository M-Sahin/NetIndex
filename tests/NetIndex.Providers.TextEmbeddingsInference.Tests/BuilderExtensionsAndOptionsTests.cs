using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.TextEmbeddingsInference.Options;
using NSubstitute;
using Xunit;

namespace NetIndex.Providers.TextEmbeddingsInference.Tests;

public class BuilderExtensionsAndOptionsTests
{
    private static (INetIndexBuilder Builder, ServiceCollection Services) NewBuilder()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);
        return (builder, services);
    }

    [Fact]
    public void UseTeiReranker_NullBuilder_Throws()
    {
        var act = () => NetIndexBuilderExtensions.UseTeiReranker(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseTeiReranker_RegistersRerankerAndAppliesOptions()
    {
        var (builder, services) = NewBuilder();

        builder.UseTeiReranker(o => o.Endpoint = "http://reranker:80");

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IDocumentReranker>().Should().BeOfType<TeiDocumentReranker>();
        provider.GetRequiredService<IOptions<TeiRerankerOptions>>().Value.Endpoint.Should().Be("http://reranker:80");
    }

    [Fact]
    public void UseTeiReranker_ConfigurationSection_BindsOptions()
    {
        var (builder, services) = NewBuilder();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tei:Endpoint"] = "http://reranker:80",
            ["Tei:FailureThreshold"] = "7",
        }).Build();

        builder.UseTeiReranker(config.GetSection("Tei"));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<TeiRerankerOptions>>().Value.FailureThreshold.Should().Be(7);
    }

    [Fact]
    public void UseTeiReranker_InvalidOptions_FailOnResolve()
    {
        var (builder, services) = NewBuilder();
        builder.UseTeiReranker(o => o.Endpoint = "not a uri");

        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<TeiRerankerOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void Defaults_MatchSpec()
    {
        var o = new TeiRerankerOptions();

        o.ConnectTimeout.Should().Be(TimeSpan.FromMilliseconds(500));
        o.RequestTimeout.Should().Be(TimeSpan.FromSeconds(5));
        o.FailureThreshold.Should().Be(3);
        o.BreakDuration.Should().Be(TimeSpan.FromSeconds(30));
        new TeiRerankerOptionsValidator().Validate(null, o).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("reranker")]
    [InlineData("ftp://reranker")]
    public void Validator_RejectsBadEndpoint(string endpoint)
    {
        var o = new TeiRerankerOptions { Endpoint = endpoint };

        new TeiRerankerOptionsValidator().Validate(null, o).Failed.Should().BeTrue();
    }

    [Fact]
    public void Validator_RejectsNonPositiveTimingsAndThreshold()
    {
        var validator = new TeiRerankerOptionsValidator();

        validator.Validate(null, new TeiRerankerOptions { ConnectTimeout = TimeSpan.Zero }).Failed.Should().BeTrue();
        validator.Validate(null, new TeiRerankerOptions { RequestTimeout = TimeSpan.Zero }).Failed.Should().BeTrue();
        validator.Validate(null, new TeiRerankerOptions { FailureThreshold = 0 }).Failed.Should().BeTrue();
        validator.Validate(null, new TeiRerankerOptions { BreakDuration = TimeSpan.Zero }).Failed.Should().BeTrue();
    }
}
