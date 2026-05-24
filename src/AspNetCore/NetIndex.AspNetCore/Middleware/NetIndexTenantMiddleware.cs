using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NetIndex.AspNetCore.Options;

namespace NetIndex.AspNetCore.Middleware;

/// <summary>
/// ASP.NET Core middleware that extracts tenant context from the incoming HTTP request
/// and stores it in <see cref="HttpContext.Items"/> for downstream resolution.
/// </summary>
/// <remarks>
/// This middleware never throws on a missing tenant header. The resolver
/// (<see cref="HttpContextTenantResolver"/>) is the authorization boundary — keeping
/// the middleware passive lets it be registered globally (e.g., for health or metrics
/// routes) without impacting unauthenticated endpoints.
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
        var tenantValue = context.Request.Headers[_options.HeaderName].ToString();
        if (!string.IsNullOrWhiteSpace(tenantValue))
        {
            context.Items[TenantContextKey] = tenantValue;
        }

        if (!string.IsNullOrEmpty(_options.ClaimsHeaderPrefix))
        {
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

                var claimValue = header.Value.ToString();
                if (string.IsNullOrWhiteSpace(claimValue))
                {
                    continue;
                }

                claims ??= new Dictionary<string, string>();
                claims[claimName] = claimValue;
            }

            if (claims is not null)
            {
                context.Items[ClaimsContextKey] = claims;
            }
        }

        return _next(context);
    }
}
