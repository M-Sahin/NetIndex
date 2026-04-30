using System.Diagnostics;

namespace NetIndex.Core.Abstractions.Telemetry;

/// <summary>
/// Provides the singleton <see cref="ActivitySource"/> used by all NetIndex packages
/// for OpenTelemetry-compatible distributed tracing.
/// </summary>
/// <remarks>
/// Every provider, storage, and ingestion package emits spans from this single source.
/// Span names follow the <c>netindex.{stage}</c> convention defined in
/// <see cref="NetIndexSpanNames"/>.
///
/// This type has zero external dependencies — it uses only <c>System.Diagnostics.Activity</c>
/// from the BCL. The OpenTelemetry SDK is added by the host application, not the framework.
/// </remarks>
public static class NetIndexActivitySource
{
    /// <summary>
    /// Gets the singleton <see cref="ActivitySource"/> instance named "NetIndex".
    /// </summary>
    /// <remarks>
    /// Repeated accesses return the same instance. <see cref="ActivitySource"/> is
    /// thread-safe for <c>StartActivity()</c> calls — no locking is required.
    /// </remarks>
    public static ActivitySource Source { get; } = new("NetIndex");
}
