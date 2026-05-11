using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetIndex.Core.Abstractions;
using NetIndex.Storage.InMemory.Options;

namespace NetIndex.Storage.InMemory;

/// <summary>
/// Extension methods for configuring in-memory storage on INetIndexBuilder.
/// </summary>
public static class NetIndexBuilderExtensions
{
    /// <summary>
    /// Registers the in-memory vector store, optionally configuring options.
    /// </summary>
    /// <param name="builder">The NetIndex builder.</param>
    /// <param name="configure">Optional delegate to configure InMemoryOptions.</param>
    /// <returns>The builder for chaining.</returns>
    public static INetIndexBuilder UseInMemoryVectorStore(this INetIndexBuilder builder, Action<InMemoryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions();

        if (configure is not null)
        {
            builder.Services.Configure<InMemoryOptions>(configure);
        }

        builder.Services.TryAddSingleton<IVectorStore, InMemoryVectorStore>();

        return builder;
    }
}