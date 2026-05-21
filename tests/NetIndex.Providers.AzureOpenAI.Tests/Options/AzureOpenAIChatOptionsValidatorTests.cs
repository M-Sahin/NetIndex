using FluentAssertions;
using NetIndex.Providers.AzureOpenAI.Options;
using Xunit;

namespace NetIndex.Providers.AzureOpenAI.Tests.Options;

public sealed class AzureOpenAIChatOptionsValidatorTests
{
    [Theory]
    [InlineData("http://example.openai.azure.com/")]
    [InlineData("file:///tmp/openai")]
    public void Validator_RejectsNonHttpsEndpoint(string endpoint)
    {
        var options = ValidOptions();
        options.Endpoint = new Uri(endpoint);

        var result = new AzureOpenAIChatOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("HTTPS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_RejectsBlankDeployment()
    {
        var options = ValidOptions();
        options.ChatDeployment = "";

        var result = new AzureOpenAIChatOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("ChatDeployment", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsNonPositiveTimeout()
    {
        var options = ValidOptions();
        options.Timeout = TimeSpan.Zero;

        var result = new AzureOpenAIChatOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("Timeout", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_AcceptsValidConfig()
    {
        var result = new AzureOpenAIChatOptionsValidator().Validate(null, ValidOptions());

        result.Succeeded.Should().BeTrue();
    }

    private static AzureOpenAIChatOptions ValidOptions() => new()
    {
        Endpoint = new Uri("https://example.openai.azure.com/"),
        ChatDeployment = "gpt-4o-mini",
        Timeout = TimeSpan.FromSeconds(120),
    };
}
