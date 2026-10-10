using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.TextEmbeddingsInference.Options;

namespace NetIndex.Providers.TextEmbeddingsInference;

/// <summary>Extension methods for configuring the TEI reranker on <see cref="INetIndexBuilder"/>.</summary>
public static class NetIndexBuilderExtensions
{
    /// <summary>Registers the Text Embeddings Inference reranker.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="configure">Optional options delegate.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    public static INetIndexBuilder UseTeiReranker(
        this INetIndexBuilder builder,
        Action<TeiRerankerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var optionsBuilder = builder.Services.AddOptions<TeiRerankerOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        Register(builder);
        return builder;
    }

    /// <summary>Registers the Text Embeddings Inference reranker using a configuration section.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="section">The configuration section to bind to <see cref="TeiRerankerOptions"/>.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="section"/> is null.</exception>
    public static INetIndexBuilder UseTeiReranker(
        this INetIndexBuilder builder,
        IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(section);

        builder.Services.AddOptions<TeiRerankerOptions>().Bind(section);

        Register(builder);
        return builder;
    }

    private static void Register(INetIndexBuilder builder)
    {
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TeiRerankerOptions>, TeiRerankerOptionsValidator>());
        builder.Services.TryAddSingleton<IDocumentReranker, TeiDocumentReranker>();
    }
}
