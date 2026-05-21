using FluentAssertions;
using NetIndex.Providers.AzureOpenAI.Options;
using Xunit;

namespace NetIndex.Providers.AzureOpenAI.Tests.Options;

public sealed class AzureOpenAIOptionsValidatorTests
{
    [Theory]
    [InlineData("http://example.openai.azure.com/")]
    [InlineData("ftp://example.openai.azure.com/")]
    public void Validator_RejectsNonHttpsEndpoint(string endpoint)
    {
        var options = ValidOptions();
        options.Endpoint = new Uri(endpoint);

        var result = new AzureOpenAIOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("HTTPS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_RejectsBlankDeployment()
    {
        var options = ValidOptions();
        options.EmbeddingDeployment = " ";

        var result = new AzureOpenAIOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("EmbeddingDeployment", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsNonPositiveTimeout()
    {
        var options = ValidOptions();
        options.Timeout = TimeSpan.Zero;

        var result = new AzureOpenAIOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("Timeout", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validator_RejectsNonPositiveDimensions(int dimensions)
    {
        var options = ValidOptions();
        options.EmbeddingDimensions = dimensions;

        var result = new AzureOpenAIOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("EmbeddingDimensions", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_AcceptsValidConfig()
    {
        var result = new AzureOpenAIOptionsValidator().Validate(null, ValidOptions());

        result.Succeeded.Should().BeTrue();
    }

    private static AzureOpenAIOptions ValidOptions() => new()
    {
        Endpoint = new Uri("https://example.openai.azure.com/"),
        EmbeddingDeployment = "text-embedding-3-small",
        Timeout = TimeSpan.FromSeconds(30),
    };
}
