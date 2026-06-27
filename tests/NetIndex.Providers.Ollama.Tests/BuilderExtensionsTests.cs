using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.Ollama;
using NetIndex.Providers.Ollama.Options;
using NSubstitute;
using Xunit;

namespace NetIndex.Providers.Ollama.Tests;

/// <summary>
/// Unit tests for <see cref="NetIndexBuilderExtensions"/>.
/// </summary>
public class BuilderExtensionsTests
{
    /// <summary>
    /// Verifies that passing a null builder throws <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public void UseOllama_WithNullBuilder_ThrowsArgumentNullException()
    {
        var act = () => NetIndexBuilderExtensions.UseOllama(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Verifies that calling <c>UseOllama</c> with no options registers <see cref="OllamaEmbeddingGenerator"/> as <see cref="IEmbeddingGenerator"/>.
    /// </summary>
    [Fact]
    public void UseOllama_WithNoOptions_RegistersDefaultOllamaEmbeddingGenerator()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);

        builder.UseOllama();

        var provider = services.BuildServiceProvider();
        var generator = provider.GetService<IEmbeddingGenerator>();
        generator.Should().BeOfType<OllamaEmbeddingGenerator>();
    }

    /// <summary>
    /// Verifies that custom options are applied and surfaced via <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>.
    /// </summary>
    [Fact]
    public void UseOllama_WithCustomOptions_SetsOptionsOnRegistration()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);

        builder.UseOllama(opts => opts.Dimensions = 512);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaOptions>>();
        options.Value.Dimensions.Should().Be(512);
    }

    /// <summary>
    /// Verifies that calling <c>UseOllama</c> twice does not register a duplicate <see cref="IEmbeddingGenerator"/>.
    /// </summary>
    [Fact]
    public void UseOllama_CalledTwice_DoesNotRegisterDuplicate()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);

        builder.UseOllama();
        builder.UseOllama();

        var registrations = services.Where(s => s.ServiceType == typeof(IEmbeddingGenerator)).ToList();
        registrations.Should().HaveCount(1);
    }

    /// <summary>
    /// Verifies that the <see cref="IConfigurationSection"/> overload binds options and registers the generator.
    /// Guards the template's documented swap line <c>UseOllama(builder.Configuration.GetSection("NetIndex:Ollama"))</c>.
    /// </summary>
    [Fact]
    public void UseOllama_WithConfigurationSection_BindsOptionsAndRegistersGenerator()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NetIndex:Ollama:Dimensions"] = "768",
            })
            .Build();
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);

        builder.UseOllama(config.GetSection("NetIndex:Ollama"));

        var provider = services.BuildServiceProvider();
        provider.GetService<IEmbeddingGenerator>().Should().BeOfType<OllamaEmbeddingGenerator>();
        provider.GetRequiredService<IOptions<OllamaOptions>>().Value.Dimensions.Should().Be(768);
    }

    /// <summary>
    /// Verifies that the <see cref="IConfigurationSection"/> overload rejects a null section.
    /// </summary>
    [Fact]
    public void UseOllama_WithNullConfigurationSection_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);

        var act = () => builder.UseOllama((IConfigurationSection)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Verifies that passing a null builder throws <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public void UseOllamaChatClient_WithNullBuilder_ThrowsArgumentNullException()
    {
        var act = () => NetIndexBuilderExtensions.UseOllamaChatClient(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Verifies that calling <c>UseOllamaChatClient</c> with no options registers <see cref="OllamaChatClient"/> as <see cref="IChatClient"/>.
    /// </summary>
    [Fact]
    public void UseOllamaChatClient_WithNoOptions_RegistersDefaultOllamaChatClient()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);

        builder.UseOllamaChatClient();

        var provider = services.BuildServiceProvider();
        var chatClient = provider.GetService<IChatClient>();
        chatClient.Should().BeOfType<OllamaChatClient>();
    }

    /// <summary>
    /// Verifies that custom options are applied and surfaced via <see cref="IOptions{TOptions}"/>.
    /// </summary>
    [Fact]
    public void UseOllamaChatClient_WithCustomOptions_SetsOptionsOnRegistration()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);

        builder.UseOllamaChatClient(opts => opts.Model = "mistral");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OllamaChatOptions>>();
        options.Value.Model.Should().Be("mistral");
    }

    /// <summary>
    /// Verifies that calling <c>UseOllamaChatClient</c> twice does not register a duplicate <see cref="IChatClient"/>
    /// or duplicate <see cref="IValidateOptions{OllamaChatOptions}"/>.
    /// </summary>
    [Fact]
    public void UseOllamaChatClient_CalledTwice_DoesNotRegisterDuplicate()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);

        builder.UseOllamaChatClient();
        builder.UseOllamaChatClient();

        var chatRegistrations = services.Where(s => s.ServiceType == typeof(IChatClient)).ToList();
        chatRegistrations.Should().HaveCount(1);

        var validatorRegistrations = services
            .Where(s => s.ServiceType == typeof(IValidateOptions<OllamaChatOptions>))
            .ToList();
        validatorRegistrations.Should().HaveCount(1);
    }
}
