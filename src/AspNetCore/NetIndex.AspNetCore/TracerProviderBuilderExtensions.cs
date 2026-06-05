using NetIndex.Core.Abstractions.Telemetry;
using OpenTelemetry.Trace;

namespace NetIndex.AspNetCore;

/// <summary>
/// Extension methods for registering NetIndex telemetry with an OpenTelemetry <see cref="TracerProviderBuilder"/>.
/// </summary>
public static class TracerProviderBuilderExtensions
{
    /// <summary>
    /// Registers the NetIndex <see cref="NetIndexActivitySource"/> with the OpenTelemetry tracer provider
    /// so that pipeline spans are captured by the host's configured exporter.
    /// </summary>
    /// <param name="builder">The <see cref="TracerProviderBuilder"/> to configure.</param>
    /// <returns>The same <see cref="TracerProviderBuilder"/> for chaining.</returns>
    /// <remarks>
    /// Call this inside your <c>services.AddOpenTelemetry().WithTracing(tracing => tracing.AddNetIndex())</c>
    /// setup. Exporter configuration (OTLP, Jaeger, Application Insights, etc.) is the host's
    /// responsibility — the NetIndex framework is exporter-agnostic.
    ///
    /// Calling this method more than once is safe; the OpenTelemetry SDK deduplicates source registrations.
    /// </remarks>
    public static TracerProviderBuilder AddNetIndex(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddSource(NetIndexActivitySource.Source.Name);
    }
}
