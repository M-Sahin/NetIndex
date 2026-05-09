using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.Ollama.Options;

namespace NetIndex.Providers.Ollama;

/// <summary>Extension methods for configuring Ollama on <see cref="INetIndexBuilder"/>.</summary>
public static class NetIndexBuilderExtensions
{
    /// <summary>Registers the Ollama embedding provider.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="configure">Optional options delegate.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    public static INetIndexBuilder UseOllama(
        this INetIndexBuilder builder,
        Action<OllamaOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var optionsBuilder = builder.Services.AddOptions<OllamaOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        // Validation runs when IOptions<OllamaOptions> is resolved during NetIndexBuilder.Build().
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OllamaOptions>, OllamaOptionsValidator>());
        builder.Services.TryAddSingleton<IEmbeddingGenerator, OllamaEmbeddingGenerator>();

        return builder;
    }
}
