using FluentAssertions;
using NetIndex.Providers.OpenAI.Options;
using Xunit;

namespace NetIndex.Providers.OpenAI.Tests.Options;

public sealed class OpenAIOptionsValidatorTests
{
    private static OpenAIOptionsValidator Validator() => new();

    [Fact]
    public void Validate_ValidOptions_ReturnsSuccess()
    {
        var options = ValidOptions();
        var result = Validator().Validate(null, options);
        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankApiKey_Fails(string apiKey)
    {
        var options = ValidOptions();
        options.ApiKey = apiKey;
        var result = Validator().Validate(null, options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ApiKey");
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("ftp://example.com")]
    public void Validate_NonHttpsEndpoint_Fails(string endpoint)
    {
        var options = ValidOptions();
        options.Endpoint = new Uri(endpoint);
        var result = Validator().Validate(null, options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Endpoint");
    }

    [Fact]
    public void Validate_NullEndpoint_Succeeds()
    {
        var options = ValidOptions();
        options.Endpoint = null;
        var result = Validator().Validate(null, options);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_HttpsCustomEndpoint_Succeeds()
    {
        var options = ValidOptions();
        options.Endpoint = new Uri("https://my-compat-server.example.com/v1/");
        var result = Validator().Validate(null, options);
        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankEmbeddingModel_Fails(string model)
    {
        var options = ValidOptions();
        options.EmbeddingModel = model;
        var result = Validator().Validate(null, options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("EmbeddingModel");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankChatModel_Fails(string model)
    {
        var options = ValidOptions();
        options.ChatModel = model;
        var result = Validator().Validate(null, options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ChatModel");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Validate_NonPositiveEmbeddingDimensions_Fails(int dims)
    {
        var options = ValidOptions();
        options.EmbeddingDimensions = dims;
        var result = Validator().Validate(null, options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("EmbeddingDimensions");
    }

    [Fact]
    public void Validate_PositiveEmbeddingDimensions_Succeeds()
    {
        var options = ValidOptions();
        options.EmbeddingDimensions = 256;
        var result = Validator().Validate(null, options);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_ZeroTimeout_Fails()
    {
        var options = ValidOptions();
        options.Timeout = TimeSpan.Zero;
        var result = Validator().Validate(null, options);
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Timeout");
    }

    private static OpenAIOptions ValidOptions() => new()
    {
        ApiKey = "sk-test",
        EmbeddingModel = "text-embedding-3-small",
        ChatModel = "gpt-4o-mini",
        Timeout = TimeSpan.FromSeconds(30),
    };
}
