using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    public object Build()
    {
        RegisterDefaults();

        try
        {
            using var provider = _services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });

            _ = provider.GetRequiredService<IOptions<NetIndexOptions>>().Value;
            return provider.GetRequiredService<NetIndexPipeline>();
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
    }

    private void RegisterDefaults()
    {
        _services.TryAddSingleton<ITenantResolver, DenyAllTenantResolver>();
        _services.TryAddSingleton<IVectorStore, InMemoryVectorStore>();
        _services.TryAddSingleton<IEmbeddingGenerator, InMemoryEmbeddingGenerator>();
        _services.TryAddSingleton<IChatClient, InMemoryChatClient>();
        _services.TryAddSingleton<NetIndexPipeline>();
    }
}
