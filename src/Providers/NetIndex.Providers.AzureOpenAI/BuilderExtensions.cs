using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.AzureOpenAI.Options;

namespace NetIndex.Providers.AzureOpenAI;

/// <summary>
/// Extension methods for configuring Azure OpenAI providers on <see cref="INetIndexBuilder"/>.
/// </summary>
public static class NetIndexBuilderExtensions
{
    /// <summary>
    /// Registers the Azure OpenAI embedding provider.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="configure">Optional options delegate.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    public static INetIndexBuilder UseAzureOpenAI(
        this INetIndexBuilder builder,
        Action<AzureOpenAIOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var optionsBuilder = builder.Services.AddOptions<AzureOpenAIOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }
        optionsBuilder.ValidateOnStart();

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AzureOpenAIOptions>, AzureOpenAIOptionsValidator>());
        builder.Services.TryAddSingleton<IEmbeddingGenerator, AzureOpenAIEmbeddingGenerator>();

        return builder;
    }

    /// <summary>
    /// Registers the Azure OpenAI embedding provider using a configuration section.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="section">The configuration section to bind.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="section"/> is null.</exception>
    public static INetIndexBuilder UseAzureOpenAI(
        this INetIndexBuilder builder,
        IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(section);

        builder.Services.AddOptions<AzureOpenAIOptions>()
            .Bind(section)
            .ValidateOnStart();

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AzureOpenAIOptions>, AzureOpenAIOptionsValidator>());
        builder.Services.TryAddSingleton<IEmbeddingGenerator, AzureOpenAIEmbeddingGenerator>();

        return builder;
    }

    /// <summary>
    /// Registers the Azure OpenAI chat client.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="configure">Optional options delegate.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    public static INetIndexBuilder UseAzureOpenAIChatClient(
        this INetIndexBuilder builder,
        Action<AzureOpenAIChatOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var optionsBuilder = builder.Services.AddOptions<AzureOpenAIChatOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }
        optionsBuilder.ValidateOnStart();

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AzureOpenAIChatOptions>, AzureOpenAIChatOptionsValidator>());
        builder.Services.TryAddSingleton<IChatClient, AzureOpenAIChatClient>();

        return builder;
    }

    /// <summary>
    /// Registers the Azure OpenAI chat client using a configuration section.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="section">The configuration section to bind.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="section"/> is null.</exception>
    public static INetIndexBuilder UseAzureOpenAIChatClient(
        this INetIndexBuilder builder,
        IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(section);

        builder.Services.AddOptions<AzureOpenAIChatOptions>()
            .Bind(section)
            .ValidateOnStart();

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AzureOpenAIChatOptions>, AzureOpenAIChatOptionsValidator>());
        builder.Services.TryAddSingleton<IChatClient, AzureOpenAIChatClient>();

        return builder;
    }
}
