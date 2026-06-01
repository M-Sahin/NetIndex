using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetIndex.AspNetCore.BackgroundServices;
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
    /// <para>
    /// Also call <c>app.UseNetIndexTenant()</c> on the <c>IApplicationBuilder</c> to register the
    /// middleware that populates <c>HttpContext.Items</c> before the pipeline runs.
    /// </para>
    /// <para>
    /// <strong>Idempotent (first call wins):</strong> the first call's options configuration takes
    /// effect. Any later call to either <c>UseAspNetCoreTenant</c> overload — including the
    /// <see cref="IConfigurationSection"/> overload — is ignored: its delegate/section is neither
    /// accumulated nor applied as an override.
    /// </para>
    /// </remarks>
    public static INetIndexBuilder UseAspNetCoreTenant(
        this INetIndexBuilder builder,
        Action<NetIndexTenantOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Idempotency guard: options delegates accumulate with each call; allow only one registration.
        if (!builder.Services.Any(d => d.ServiceType == typeof(AspNetCoreTenantOptionsMarker)))
        {
            builder.Services.AddSingleton<AspNetCoreTenantOptionsMarker>();
            var optionsBuilder = builder.Services.AddOptions<NetIndexTenantOptions>();
            if (configure is not null)
            {
                optionsBuilder.Configure(configure);
            }
            optionsBuilder.ValidateOnStart();
        }

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
    /// <para>
    /// Also call <c>app.UseNetIndexTenant()</c> on the <c>IApplicationBuilder</c> to register the
    /// middleware that populates <c>HttpContext.Items</c> before the pipeline runs.
    /// </para>
    /// <para>
    /// <strong>Idempotent (first call wins):</strong> the first call's options configuration takes
    /// effect. Any later call to either <c>UseAspNetCoreTenant</c> overload — including the delegate
    /// overload — is ignored: its delegate/section is neither accumulated nor applied as an override.
    /// </para>
    /// </remarks>
    public static INetIndexBuilder UseAspNetCoreTenant(
        this INetIndexBuilder builder,
        IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(section);

        // Idempotency guard: options delegates accumulate with each call; allow only one registration.
        if (!builder.Services.Any(d => d.ServiceType == typeof(AspNetCoreTenantOptionsMarker)))
        {
            builder.Services.AddSingleton<AspNetCoreTenantOptionsMarker>();
            builder.Services.AddOptions<NetIndexTenantOptions>()
                .Bind(section)
                .ValidateOnStart();
        }

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<NetIndexTenantOptions>, NetIndexTenantOptionsValidator>());
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton<ITenantResolver, HttpContextTenantResolver>();

        return builder;
    }

    /// <summary>
    /// Registers the background ingestion queue and the hosted service that drains it, so document
    /// intake can be enqueued and processed off the request thread.
    /// </summary>
    /// <param name="builder">The NetIndex builder.</param>
    /// <param name="configure">Optional delegate to configure <see cref="BackgroundIngestionOptions"/>.</param>
    /// <returns>The same builder for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Inject <see cref="IIngestionQueue"/> into a request handler and call
    /// <c>EnqueueAsync(document)</c> to queue work; the hosted service ingests it in the background.
    /// The queue captures the current request's tenant context (as populated by
    /// <c>UseAspNetCoreTenant</c>) and replays it during background ingestion. A custom
    /// <see cref="ITenantResolver"/> that does not read <c>HttpContext.Items</c> must be
    /// background-safe on its own.
    /// </para>
    /// <para>
    /// <strong>Shutdown semantics (v1, at-most-once):</strong> The backing queue is in-memory and
    /// non-durable. When the host stops, the <c>stoppingToken</c> is cancelled: the in-flight
    /// document's <c>IngestAsync</c> is cancelled mid-operation, and any items still queued are
    /// silently dropped. There is no compensation, no retry on restart, and no durability guarantee
    /// across process restarts. Operators must tolerate the possibility of a document being lost on
    /// unplanned shutdown without having been fully ingested.
    /// </para>
    /// <para>
    /// <strong>Idempotent (first call wins):</strong> the first call's options configuration takes
    /// effect. Any later call to either <c>UseBackgroundIngestion</c> overload — including the
    /// <see cref="IConfigurationSection"/> overload — is ignored: its delegate/section is neither
    /// accumulated nor applied as an override.
    /// </para>
    /// </remarks>
    public static INetIndexBuilder UseBackgroundIngestion(
        this INetIndexBuilder builder,
        Action<BackgroundIngestionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Idempotency guard: options delegates accumulate with each call; allow only one registration.
        if (!builder.Services.Any(d => d.ServiceType == typeof(BackgroundIngestionOptionsMarker)))
        {
            builder.Services.AddSingleton<BackgroundIngestionOptionsMarker>();
            var optionsBuilder = builder.Services.AddOptions<BackgroundIngestionOptions>();
            if (configure is not null)
            {
                optionsBuilder.Configure(configure);
            }
            optionsBuilder.ValidateOnStart();
        }

        RegisterBackgroundIngestionServices(builder.Services);

        return builder;
    }

    /// <summary>
    /// Registers the background ingestion queue and hosted service, binding options to a
    /// configuration section.
    /// </summary>
    /// <param name="builder">The NetIndex builder.</param>
    /// <param name="section">The configuration section to bind to <see cref="BackgroundIngestionOptions"/>.</param>
    /// <returns>The same builder for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="section"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Inject <see cref="IIngestionQueue"/> into a request handler and call
    /// <c>EnqueueAsync(document)</c> to queue work; the hosted service ingests it in the background.
    /// </para>
    /// <para>
    /// <strong>Shutdown semantics (v1, at-most-once):</strong> The backing queue is in-memory and
    /// non-durable. When the host stops, the <c>stoppingToken</c> is cancelled: the in-flight
    /// document's <c>IngestAsync</c> is cancelled mid-operation, and any items still queued are
    /// silently dropped. There is no compensation, no retry on restart, and no durability guarantee
    /// across process restarts. Operators must tolerate the possibility of a document being lost on
    /// unplanned shutdown without having been fully ingested.
    /// </para>
    /// <para>
    /// <strong>Idempotent (first call wins):</strong> the first call's options configuration takes
    /// effect. Any later call to either <c>UseBackgroundIngestion</c> overload — including the
    /// delegate overload — is ignored: its delegate/section is neither accumulated nor applied as
    /// an override.
    /// </para>
    /// </remarks>
    public static INetIndexBuilder UseBackgroundIngestion(
        this INetIndexBuilder builder,
        IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(section);

        // Idempotency guard: options delegates accumulate with each call; allow only one registration.
        if (!builder.Services.Any(d => d.ServiceType == typeof(BackgroundIngestionOptionsMarker)))
        {
            builder.Services.AddSingleton<BackgroundIngestionOptionsMarker>();
            builder.Services.AddOptions<BackgroundIngestionOptions>()
                .Bind(section)
                .ValidateOnStart();
        }

        RegisterBackgroundIngestionServices(builder.Services);

        return builder;
    }

    private static void RegisterBackgroundIngestionServices(IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BackgroundIngestionOptions>, BackgroundIngestionOptionsValidator>());
        services.AddHttpContextAccessor();
        services.TryAddSingleton<IIngestionQueue, ChannelIngestionQueue>();
        services.AddHostedService<IngestionHostedService>();
    }

    // Marker types used as idempotency tokens for options-registration guards.
    // TryAddSingleton semantics don't cover AddOptions().Configure() accumulation,
    // so we use a dedicated marker per extension method family.
    private sealed class AspNetCoreTenantOptionsMarker { }
    private sealed class BackgroundIngestionOptionsMarker { }
}
