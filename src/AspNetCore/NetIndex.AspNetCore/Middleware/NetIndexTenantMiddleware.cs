using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NetIndex.AspNetCore.Options;

namespace NetIndex.AspNetCore.Middleware;

/// <summary>
/// ASP.NET Core middleware that extracts tenant context from the incoming HTTP request
/// and stores it in <see cref="HttpContext.Items"/> for downstream resolution.
/// </summary>
/// <remarks>
/// This middleware never throws on a missing or malformed tenant header. The resolver
/// (<see cref="HttpContextTenantResolver"/>) is the authorization boundary — keeping
/// the middleware passive lets it be registered globally (e.g., for health or metrics
/// routes) without impacting unauthenticated endpoints.
/// When a malformed condition is detected (multi-value tenant header or claim-key collision)
/// a typed marker is placed in <see cref="HttpContext.Items"/> so the resolver can throw
/// a structured <see cref="NetIndex.Core.Abstractions.NetIndexAuthorizationException"/>.
/// </remarks>
public sealed class NetIndexTenantMiddleware
{
    /// <summary>
    /// The key under which the resolved tenant ID is stored in <see cref="HttpContext.Items"/>.
    /// </summary>
    public const string TenantContextKey = "NetIndex.Tenant.Id";

    /// <summary>
    /// The key under which the forwarded claims dictionary is stored in <see cref="HttpContext.Items"/>.
    /// </summary>
    public const string ClaimsContextKey = "NetIndex.Tenant.Claims";

    /// <summary>
    /// The key under which a malformed-request marker is stored in <see cref="HttpContext.Items"/>.
    /// When this key is present, <see cref="HttpContextTenantResolver"/> throws
    /// <see cref="NetIndex.Core.Abstractions.NetIndexAuthorizationException"/> with the stored reason.
    /// </summary>
    public const string MalformedTenantKey = "NetIndex.Tenant.Malformed";

    private readonly RequestDelegate _next;
    private readonly NetIndexTenantOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="NetIndexTenantMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="options">The tenant options snapshot.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="next"/> or <paramref name="options"/> is null.</exception>
    public NetIndexTenantMiddleware(RequestDelegate next, IOptions<NetIndexTenantOptions> options)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(options);
        _next = next;
        _options = options.Value;
    }

    /// <summary>
    /// Processes the request: reads the tenant header and optional claims headers,
    /// then forwards to the next middleware.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    public Task InvokeAsync(HttpContext context)
    {
        ReadTenantHeader(context);
        ReadClaimHeaders(context);
        return _next(context);
    }

    private void ReadTenantHeader(HttpContext context)
    {
        var tenantValues = context.Request.Headers[_options.HeaderName];

        if (tenantValues.Count == 0)
        {
            return;
        }

        // Multi-value tenant header is a malformed request (AC-5): record a marker so the
        // resolver can reject it with a structured exception rather than silently comma-joining.
        if (tenantValues.Count > 1)
        {
            context.Items[MalformedTenantKey] = "MultiValueTenantHeader";
            return;
        }

        // Trim surrounding whitespace from a single-value tenant header (AC-5).
        var tenantValue = tenantValues[0]?.Trim();
        if (!string.IsNullOrEmpty(tenantValue))
        {
            context.Items[TenantContextKey] = tenantValue;
        }
    }

    private void ReadClaimHeaders(HttpContext context)
    {
        // Claim-header pass-through is OFF by default (AC-1).
        // It is only active when explicitly opted-in AND the request comes from a trusted proxy.
        if (!_options.AcceptClaimHeaders || !IsFromTrustedProxy(context))
        {
            return;
        }

        // ClaimsHeaderPrefix whitespace = disabled (AC-4 / validator also enforces this at startup).
        if (string.IsNullOrWhiteSpace(_options.ClaimsHeaderPrefix))
        {
            return;
        }

        // OrdinalIgnoreCase dict so that claim key comparisons are intent-signalling (AC-3).
        Dictionary<string, string>? claims = null;
        foreach (var header in context.Request.Headers)
        {
            if (!header.Key.StartsWith(_options.ClaimsHeaderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var claimName = header.Key.Substring(_options.ClaimsHeaderPrefix.Length).ToLowerInvariant();
            if (string.IsNullOrEmpty(claimName))
            {
                continue;
            }

            // Multi-value claim header means two case-variant headers were sent
            // (e.g. X-NetIndex-Claim-Role: admin AND X-NetIndex-Claim-ROLE: evil), which
            // HTTP coalesces into one entry with multiple values. Treat as a collision (AC-3).
            if (header.Value.Count > 1)
            {
                context.Items[MalformedTenantKey] = "ClaimKeyCollision";
                return;
            }

            var claimValue = header.Value.ToString();
            if (string.IsNullOrWhiteSpace(claimValue))
            {
                continue;
            }

            claims ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Belt-and-suspenders collision check for the (unlikely) case where two distinct
            // header names produce the same claim key after prefix-strip + ToLowerInvariant.
            if (claims.ContainsKey(claimName))
            {
                context.Items[MalformedTenantKey] = "ClaimKeyCollision";
                return;
            }

            claims[claimName] = claimValue;
        }

        if (claims is not null)
        {
            context.Items[ClaimsContextKey] = claims;
        }
    }

    private bool IsFromTrustedProxy(HttpContext context)
    {
        if (_options.TrustedProxies.Count == 0)
        {
            return false;
        }

        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is null)
        {
            return false;
        }

        // Normalize IPv4-mapped IPv6 addresses (e.g. ::ffff:127.0.0.1 → 127.0.0.1).
        if (remoteIp.IsIPv4MappedToIPv6)
        {
            remoteIp = remoteIp.MapToIPv4();
        }

        var remoteIpStr = remoteIp.ToString();
        foreach (var trusted in _options.TrustedProxies)
        {
            if (string.Equals(trusted, remoteIpStr, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Also compare as parsed IPAddress to handle format differences (e.g. "::1" vs "0:0:0:0:0:0:0:1").
            if (IPAddress.TryParse(trusted, out var trustedParsed) &&
                trustedParsed.Equals(remoteIp))
            {
                return true;
            }
        }

        return false;
    }
}
