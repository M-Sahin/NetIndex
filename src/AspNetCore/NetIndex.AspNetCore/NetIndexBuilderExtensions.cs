using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetIndex.AspNetCore.Options;
using NetIndex.Core.Abstractions;

namespace NetIndex.AspNetCore;

/// <summary>
/// Extension methods for configuring ASP.NET Core tenant middleware on <see cref="INetIndexBuilder"/>.
/// </summary>
public static class NetIndexBuilderExtensions
{
    /// <summary>
    /// Registers the ASP.NET Core HTTP-context-based tenant resolver and options.
    /// </summary>
    /// <param name="builder">The NetIndex builder.</param>
    /// <param name="configure">Optional delegate to configure <see cref="NetIndexTenantOptions"/>.</param>
    /// <returns>The same builder for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    /// <remarks>
    /// Also call <c>app.UseNetIndexTenant()</c> on the <c>IApplicationBuilder</c> to register the
    /// middleware that populates <c>HttpContext.Items</c> before the pipeline runs.
    /// </remarks>
    public static INetIndexBuilder UseAspNetCoreTenant(
        this INetIndexBuilder builder,
        Action<NetIndexTenantOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var optionsBuilder = builder.Services.AddOptions<NetIndexTenantOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }
        optionsBuilder.ValidateOnStart();

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<NetIndexTenantOptions>, NetIndexTenantOptionsValidator>());
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton<ITenantResolver, HttpContextTenantResolver>();

        return builder;
    }

    /// <summary>
    /// Registers the ASP.NET Core HTTP-context-based tenant resolver bound to a configuration section.
    /// </summary>
    /// <param name="builder">The NetIndex builder.</param>
    /// <param name="section">The configuration section to bind to <see cref="NetIndexTenantOptions"/>.</param>
    /// <returns>The same builder for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="section"/> is null.</exception>
    /// <remarks>
    /// Also call <c>app.UseNetIndexTenant()</c> on the <c>IApplicationBuilder</c> to register the
    /// middleware that populates <c>HttpContext.Items</c> before the pipeline runs.
    /// </remarks>
    public static INetIndexBuilder UseAspNetCoreTenant(
        this INetIndexBuilder builder,
        IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(section);

        builder.Services.AddOptions<NetIndexTenantOptions>()
            .Bind(section)
            .ValidateOnStart();

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<NetIndexTenantOptions>, NetIndexTenantOptionsValidator>());
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton<ITenantResolver, HttpContextTenantResolver>();

        return builder;
    }
}
