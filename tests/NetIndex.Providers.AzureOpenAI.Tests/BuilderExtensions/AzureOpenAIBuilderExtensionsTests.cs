using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.AzureOpenAI.Options;
using NSubstitute;
using Xunit;

namespace NetIndex.Providers.AzureOpenAI.Tests.BuilderExtensions;

public sealed class AzureOpenAIBuilderExtensionsTests
{
    [Fact]
    public void UseAzureOpenAI_RegistersServices()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);

        builder.UseAzureOpenAI(options =>
        {
            options.Endpoint = new Uri("https://example.openai.azure.com/");
            options.EmbeddingDeployment = "text-embedding-3-small";
        });

        services.Should().Contain(s => s.ServiceType == typeof(IEmbeddingGenerator) && s.ImplementationType == typeof(AzureOpenAIEmbeddingGenerator));
        services.Where(s => s.ServiceType == typeof(IValidateOptions<AzureOpenAIOptions>)).Should().HaveCount(1);
    }

    [Fact]
    public void UseAzureOpenAI_IConfigurationSectionOverload_BindsSection()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);
        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureOpenAI:Endpoint"] = "https://example.openai.azure.com/",
                ["AzureOpenAI:EmbeddingDeployment"] = "text-embedding-3-small",
                ["AzureOpenAI:EmbeddingDimensions"] = "256",
            })
            .Build()
            .GetSection("AzureOpenAI");

        builder.UseAzureOpenAI(section);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AzureOpenAIOptions>>().Value;
        options.Endpoint.Should().Be(new Uri("https://example.openai.azure.com/"));
        options.EmbeddingDeployment.Should().Be("text-embedding-3-small");
        options.EmbeddingDimensions.Should().Be(256);
    }

    [Fact]
    public void UseAzureOpenAI_CalledTwice_RegistersSingleValidator()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);

        builder.UseAzureOpenAI();
        builder.UseAzureOpenAI();

        services.Where(s => s.ServiceType == typeof(IValidateOptions<AzureOpenAIOptions>)).Should().HaveCount(1);
    }

    [Fact]
    public void UseAzureOpenAIChatClient_RegistersServices()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);

        builder.UseAzureOpenAIChatClient(options =>
        {
            options.Endpoint = new Uri("https://example.openai.azure.com/");
            options.ChatDeployment = "gpt-4o-mini";
        });

        services.Should().Contain(s => s.ServiceType == typeof(IChatClient) && s.ImplementationType == typeof(AzureOpenAIChatClient));
        services.Where(s => s.ServiceType == typeof(IValidateOptions<AzureOpenAIChatOptions>)).Should().HaveCount(1);
    }

    [Fact]
    public void UseAzureOpenAIChatClient_IConfigurationSectionOverload_BindsSection()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);
        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureOpenAI:Endpoint"] = "https://example.openai.azure.com/",
                ["AzureOpenAI:ChatDeployment"] = "gpt-4o-mini",
                ["AzureOpenAI:Timeout"] = "00:03:00",
            })
            .Build()
            .GetSection("AzureOpenAI");

        builder.UseAzureOpenAIChatClient(section);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AzureOpenAIChatOptions>>().Value;
        options.Endpoint.Should().Be(new Uri("https://example.openai.azure.com/"));
        options.ChatDeployment.Should().Be("gpt-4o-mini");
        options.Timeout.Should().Be(TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void UseAzureOpenAIChatClient_CalledTwice_RegistersSingleValidator()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);

        builder.UseAzureOpenAIChatClient();
        builder.UseAzureOpenAIChatClient();

        services.Where(s => s.ServiceType == typeof(IValidateOptions<AzureOpenAIChatOptions>)).Should().HaveCount(1);
    }
}
