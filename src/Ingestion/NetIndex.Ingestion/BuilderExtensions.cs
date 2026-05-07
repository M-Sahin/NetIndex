using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Options;
using NetIndex.Ingestion.Strategies;

namespace NetIndex.Ingestion;

/// <summary>
/// Extension methods for registering chunking services on <see cref="INetIndexBuilder"/>.
/// </summary>
public static class NetIndexBuilderExtensions
{
    /// <summary>
    /// Registers a chunking strategy and its configuration on the pipeline builder.
    /// </summary>
    /// <param name="builder">The <see cref="INetIndexBuilder"/> to configure.</param>
    /// <param name="configure">A delegate to configure <see cref="ChunkingConfiguration"/>.</param>
    /// <returns>The same <see cref="INetIndexBuilder"/> for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="configure"/> is null.</exception>
    public static INetIndexBuilder UseChunking(
        this INetIndexBuilder builder,
        Action<ChunkingConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.Configure(configure);

        // Register concrete strategies so the factory can resolve them
        builder.Services.TryAddSingleton<FixedSizeChunkingStrategy>();
        builder.Services.TryAddSingleton<SemanticChunkingStrategy>();
        builder.Services.TryAddSingleton<RecursiveChunkingStrategy>();

        // Register the selected strategy via factory pattern
        builder.Services.TryAddSingleton<IChunkingStrategy>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<ChunkingConfiguration>>().Value;
            return config.SelectedStrategy switch
            {
                ChunkingStrategyType.FixedSize => sp.GetRequiredService<FixedSizeChunkingStrategy>(),
                ChunkingStrategyType.Semantic => sp.GetRequiredService<SemanticChunkingStrategy>(),
                ChunkingStrategyType.Recursive => sp.GetRequiredService<RecursiveChunkingStrategy>(),
                _ => throw new NetIndexConfigurationException(
                    $"Unknown chunking strategy: {config.SelectedStrategy}",
                    "SelectedStrategy",
                    "FixedSize, Semantic, or Recursive",
                    config.SelectedStrategy.ToString())
            };
        });

        return builder;
    }
}