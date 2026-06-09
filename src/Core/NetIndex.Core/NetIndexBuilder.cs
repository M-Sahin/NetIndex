using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Core.NullObjects;
using NetIndex.Core.Options;

namespace NetIndex.Core;

/// <summary>
/// Default implementation of <see cref="INetIndexBuilder"/>.
/// </summary>
public sealed class NetIndexBuilder : INetIndexBuilder
{
    private readonly IServiceCollection _services;
    private bool _built;

    /// <summary>
    /// Initializes a new instance of the <see cref="NetIndexBuilder"/> class.
    /// </summary>
    /// <param name="services">Target service collection.</param>
    public NetIndexBuilder(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public IServiceCollection Services => _services;

    /// <inheritdoc />
    /// <remarks>
    /// Validates the service collection (ValidateOnBuild + ValidateScopes) using a temporary
    /// provider that is disposed immediately. <c>NetIndexPipeline</c> is registered as a
    /// singleton and will be resolved by the host's <c>IServiceProvider</c> at runtime.
    /// Returns <c>this</c> to support fluent chaining; callers resolve the pipeline from DI.
    /// </remarks>
    public object Build()
    {
        if (_built)
        {
            throw new InvalidOperationException(
                "Build() has already been called on this builder. Create a new builder instance to reconfigure.");
        }

        _built = true;
        RegisterDefaults();

        try
        {
            // Temporary provider for validation only — disposed after validation passes.
            // The host's IServiceProvider resolves NetIndexPipeline at runtime.
            using (var validationProvider = _services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            }))
            {
                _ = validationProvider.GetRequiredService<IOptions<NetIndexOptions>>().Value;
                foreach (var validator in validationProvider.GetServices<INetIndexBuildValidator>())
                {
                    validator.Validate();
                }

                // Validate dimension consistency between embedding generator and vector store (FR11).
                var embeddingGenerator = validationProvider.GetRequiredService<IEmbeddingGenerator>();
                var vectorStore = validationProvider.GetRequiredService<IVectorStore>();

                if (embeddingGenerator.Dimensions <= 0)
                {
                    throw new NetIndexConfigurationException(
                        $"Embedding provider reports {embeddingGenerator.Dimensions} dimensions. Dimensions must be greater than zero.",
                        "Dimensions",
                        "> 0",
                        embeddingGenerator.Dimensions);
                }

                if (vectorStore.Dimensions <= 0)
                {
                    throw new NetIndexConfigurationException(
                        $"Vector store reports {vectorStore.Dimensions} dimensions. Dimensions must be greater than zero.",
                        "Dimensions",
                        "> 0",
                        vectorStore.Dimensions);
                }

                if (embeddingGenerator.Dimensions != vectorStore.Dimensions)
                {
                    throw new NetIndexConfigurationException(
                        $"Embedding dimension mismatch: configured store expects {vectorStore.Dimensions}, provider returns {embeddingGenerator.Dimensions}. Full re-index required when switching embedding providers.",
                        "Dimensions",
                        vectorStore.Dimensions,
                        embeddingGenerator.Dimensions);
                }
            }
        }
        catch (NetIndexConfigurationException)
        {
            throw;
        }
        catch (OptionsValidationException exception)
        {
            throw new NetIndexConfigurationException(
                "NetIndex configuration validation failed during Build().",
                nameof(NetIndexOptions),
                "Valid NetIndexOptions",
                string.Join("; ", exception.Failures),
                exception);
        }
        catch (Exception exception)
        {
            throw new NetIndexConfigurationException(
                "NetIndex service registration is invalid. Check for missing services or circular dependencies.",
                nameof(IServiceCollection),
                "Valid service registrations",
                exception.Message,
                exception);
        }

        return this;
    }

    private void RegisterDefaults()
    {
        _services.TryAddSingleton<ITenantResolver, DenyAllTenantResolver>();
        _services.TryAddSingleton<IVectorStore, InMemoryVectorStore>();
        _services.TryAddSingleton<IEmbeddingGenerator, InMemoryEmbeddingGenerator>();
        _services.TryAddSingleton<IChatClient, InMemoryChatClient>();
        _services.TryAddSingleton<IChunkingStrategy, PassThroughChunkingStrategy>();
        _services.TryAddSingleton<INetIndexPipeline>(sp =>
        {
            var tenantResolver = sp.GetRequiredService<ITenantResolver>();
            var chunkingStrategy = sp.GetService<IChunkingStrategy>();
            var embeddingGenerator = sp.GetRequiredService<IEmbeddingGenerator>();
            var vectorStore = sp.GetRequiredService<IVectorStore>();
            var chatClient = sp.GetRequiredService<IChatClient>();
            var reranker = sp.GetService<IDocumentReranker>();
            var tenantFilteringOptions = sp.GetService<TenantFilteringOptions>();
            var logger = sp.GetService<ILogger<NetIndexPipeline>>();

            return new NetIndexPipeline(
                tenantResolver, chunkingStrategy, embeddingGenerator, vectorStore, chatClient, reranker,
                tenantFilteringOptions, logger);
        });
        _services.TryAddSingleton<NetIndexPipeline>(sp =>
            (NetIndexPipeline)sp.GetRequiredService<INetIndexPipeline>());
    }
}
