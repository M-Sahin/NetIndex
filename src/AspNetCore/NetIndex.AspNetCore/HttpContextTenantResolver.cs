using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NetIndex.AspNetCore.Middleware;
using NetIndex.AspNetCore.Options;
using NetIndex.Core.Abstractions;

namespace NetIndex.AspNetCore;

/// <summary>
/// <see cref="ITenantResolver"/> implementation that reads tenant context from the current
/// ASP.NET Core <see cref="HttpContext"/> populated by <see cref="NetIndexTenantMiddleware"/>.
/// </summary>
/// <remarks>
/// Register this resolver by calling <c>net.UseAspNetCoreTenant()</c> inside
/// <c>services.AddNetIndex(net =&gt; ...)</c>, then add <c>app.UseNetIndexTenant()</c>
/// to the request pipeline so the middleware populates <see cref="HttpContext.Items"/>
/// before any pipeline call reaches a resolver.
/// </remarks>
public sealed class HttpContextTenantResolver : ITenantResolver
{
    private static readonly IReadOnlyDictionary<string, string> EmptyClaims =
        new Dictionary<string, string>(0);

    private readonly IHttpContextAccessor _accessor;
    private readonly NetIndexTenantOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="HttpContextTenantResolver"/>.
    /// </summary>
    /// <param name="accessor">The HTTP context accessor.</param>
    /// <param name="options">The tenant options snapshot.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accessor"/> or <paramref name="options"/> is null.</exception>
    public HttpContextTenantResolver(IHttpContextAccessor accessor, IOptions<NetIndexTenantOptions> options)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(options);
        _accessor = accessor;
        _options = options.Value;
    }

    /// <inheritdoc />
    public Task<string> ResolveTenantIdAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ctx = _accessor.HttpContext;
        if (ctx is null)
        {
            throw new NetIndexAuthorizationException(
                "No active HttpContext. HttpContextTenantResolver requires a request-scoped HttpContext " +
                "(did you forget to register inside the ASP.NET Core request pipeline, or are you calling " +
                "the pipeline from a background service?).",
                tenantId: null,
                requiredClaim: null,
                failureReason: "NoHttpContext");
        }

        if (ctx.Items[NetIndexTenantMiddleware.TenantContextKey] is not string s ||
            string.IsNullOrWhiteSpace(s))
        {
            throw new NetIndexAuthorizationException(
                $"Request did not include the '{_options.HeaderName}' header. " +
                "Configure your gateway or client to forward a tenant identifier.",
                tenantId: null,
                requiredClaim: _options.HeaderName,
                failureReason: "MissingTenantHeader");
        }

        return Task.FromResult(s);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> ResolveClaimsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ctx = _accessor.HttpContext;
        if (ctx is null)
        {
            throw new NetIndexAuthorizationException(
                "No active HttpContext. HttpContextTenantResolver requires a request-scoped HttpContext " +
                "(did you forget to register inside the ASP.NET Core request pipeline, or are you calling " +
                "the pipeline from a background service?).",
                tenantId: null,
                requiredClaim: null,
                failureReason: "NoHttpContext");
        }

        if (ctx.Items[NetIndexTenantMiddleware.ClaimsContextKey] is IReadOnlyDictionary<string, string> claims)
        {
            return Task.FromResult(claims);
        }

        return Task.FromResult(EmptyClaims);
    }
}
