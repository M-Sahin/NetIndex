using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.OpenAI.Options;

namespace NetIndex.Providers.OpenAI;

/// <summary>
/// Extension methods for configuring standard OpenAI providers on <see cref="INetIndexBuilder"/>.
/// </summary>
public static class NetIndexBuilderExtensions
{
    /// <summary>
    /// Registers the standard OpenAI embedding provider and chat client.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="configure">Optional options delegate.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    public static INetIndexBuilder UseOpenAI(
        this INetIndexBuilder builder,
        Action<OpenAIOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var optionsBuilder = builder.Services.AddOptions<OpenAIOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }
        optionsBuilder.ValidateOnStart();

        RegisterCoreServices(builder.Services);
        return builder;
    }

    /// <summary>
    /// Registers the standard OpenAI embedding provider and chat client using a configuration section.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="section">The configuration section to bind.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="section"/> is null.</exception>
    public static INetIndexBuilder UseOpenAI(
        this INetIndexBuilder builder,
        IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(section);

        builder.Services.AddOptions<OpenAIOptions>()
            .Bind(section)
            .ValidateOnStart();

        RegisterCoreServices(builder.Services);
        return builder;
    }

    private static void RegisterCoreServices(IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OpenAIOptions>, OpenAIOptionsValidator>());
        services.TryAddSingleton<IEmbeddingGenerator, OpenAIEmbeddingGenerator>();
        services.TryAddSingleton<IChatClient, OpenAIChatClient>();
    }
}
