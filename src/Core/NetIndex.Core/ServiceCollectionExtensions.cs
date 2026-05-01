using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Core.Options;

namespace NetIndex.Core;

/// <summary>
/// Service collection entry point for NetIndex fluent configuration.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers NetIndex core services and returns a fluent builder.
    /// </summary>
    /// <param name="services">Target service collection.</param>
    /// <param name="configure">Optional fluent configuration callback.</param>
    /// <returns>A NetIndex builder for fluent <c>Use{Feature}(...)</c> chaining.</returns>
    public static INetIndexBuilder AddNetIndex(
        this IServiceCollection services,
        Action<INetIndexBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<NetIndexOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<NetIndexOptions>, NetIndexOptionsValidator>());

        var builder = new NetIndexBuilder(services);
        configure?.Invoke(builder);
        return builder;
    }
}
