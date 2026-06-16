using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.OpenAI.Options;
using NSubstitute;
using Xunit;

namespace NetIndex.Providers.OpenAI.Tests.BuilderExtensions;

public sealed class OpenAIBuilderExtensionsTests
{
    [Fact]
    public void UseOpenAI_RegistersEmbeddingGeneratorAndChatClient()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);

        builder.UseOpenAI(options => options.ApiKey = "sk-test");

        services.Should().Contain(s =>
            s.ServiceType == typeof(IEmbeddingGenerator) &&
            s.ImplementationType == typeof(OpenAIEmbeddingGenerator));
        services.Should().Contain(s =>
            s.ServiceType == typeof(IChatClient) &&
            s.ImplementationType == typeof(OpenAIChatClient));
        services.Where(s => s.ServiceType == typeof(IValidateOptions<OpenAIOptions>)).Should().HaveCount(1);
    }

    [Fact]
    public void UseOpenAI_IConfigurationSectionOverload_BindsSection()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);
        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = "sk-test",
                ["OpenAI:EmbeddingModel"] = "text-embedding-3-large",
                ["OpenAI:ChatModel"] = "gpt-4o",
                ["OpenAI:EmbeddingDimensions"] = "512",
            })
            .Build()
            .GetSection("OpenAI");

        builder.UseOpenAI(section);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OpenAIOptions>>().Value;
        options.ApiKey.Should().Be("sk-test");
        options.EmbeddingModel.Should().Be("text-embedding-3-large");
        options.ChatModel.Should().Be("gpt-4o");
        options.EmbeddingDimensions.Should().Be(512);
    }

    [Fact]
    public void UseOpenAI_CalledTwice_RegistersSingleValidatorAndProviders()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);

        builder.UseOpenAI();
        builder.UseOpenAI();

        services.Where(s => s.ServiceType == typeof(IValidateOptions<OpenAIOptions>)).Should().HaveCount(1);
        services.Where(s => s.ServiceType == typeof(IEmbeddingGenerator)).Should().HaveCount(1);
        services.Where(s => s.ServiceType == typeof(IChatClient)).Should().HaveCount(1);
    }

    [Fact]
    public void UseOpenAI_SetsDefaults_EmbeddingModelAndChatModel()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);

        builder.UseOpenAI(opts => opts.ApiKey = "sk-test");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OpenAIOptions>>().Value;
        options.EmbeddingModel.Should().Be("text-embedding-3-small");
        options.ChatModel.Should().Be("gpt-4o-mini");
    }
}
