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
    /// <remarks>
    /// Validates the service collection (ValidateOnBuild + ValidateScopes) using a temporary
    /// provider that is disposed immediately. <c>NetIndexPipeline</c> is registered as a
    /// singleton and will be resolved by the host's <c>IServiceProvider</c> at runtime.
    /// Returns <c>this</c> to support fluent chaining; callers resolve the pipeline from DI.
    /// </remarks>
    public object Build()
    {
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
            }
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

        return this;
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
