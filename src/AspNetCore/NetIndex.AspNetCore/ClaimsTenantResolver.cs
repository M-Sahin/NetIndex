using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NetIndex.AspNetCore.Options;
using NetIndex.Core.Abstractions;

namespace NetIndex.AspNetCore;

/// <summary>
/// <see cref="ITenantResolver"/> implementation that resolves the tenant ID from the authenticated
/// <see cref="System.Security.Claims.ClaimsPrincipal"/> on the current HTTP request.
/// </summary>
/// <remarks>
/// Register this resolver by calling <c>net.UseClaimsTenant()</c> inside
/// <c>services.AddNetIndex(net =&gt; ...)</c>. The application must authenticate requests (e.g.,
/// via JWT bearer middleware) before the NetIndex pipeline runs so that
/// <c>HttpContext.User.Identity.IsAuthenticated</c> is <c>true</c>.
/// </remarks>
public sealed class ClaimsTenantResolver : ITenantResolver
{
    private readonly IHttpContextAccessor _accessor;
    private readonly ClaimsTenantOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="ClaimsTenantResolver"/>.
    /// </summary>
    /// <param name="accessor">The HTTP context accessor.</param>
    /// <param name="options">The claims tenant options.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accessor"/> or <paramref name="options"/> is null.</exception>
    public ClaimsTenantResolver(IHttpContextAccessor accessor, IOptions<ClaimsTenantOptions> options)
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
                "No active HttpContext. ClaimsTenantResolver requires a request-scoped HttpContext.",
                tenantId: null,
                requiredClaim: null,
                failureReason: "NoHttpContext");
        }

        var identity = ctx.User.Identity;
        if (identity is null || !identity.IsAuthenticated)
        {
            throw new NetIndexAuthorizationException(
                "The current principal is not authenticated. Ensure authentication middleware runs before the NetIndex pipeline.",
                tenantId: null,
                requiredClaim: _options.TenantClaimType,
                failureReason: "Unauthenticated");
        }

        var tenantId = ctx.User.FindFirst(_options.TenantClaimType)?.Value;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new NetIndexAuthorizationException(
                $"The authenticated principal does not carry a '{_options.TenantClaimType}' claim. " +
                "Configure your identity provider to include the tenant identifier claim.",
                tenantId: null,
                requiredClaim: _options.TenantClaimType,
                failureReason: "MissingTenantIdClaim");
        }

        return Task.FromResult(tenantId);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> ResolveClaimsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ctx = _accessor.HttpContext;
        if (ctx is null)
        {
            throw new NetIndexAuthorizationException(
                "No active HttpContext. ClaimsTenantResolver requires a request-scoped HttpContext.",
                tenantId: null,
                requiredClaim: null,
                failureReason: "NoHttpContext");
        }

        // Defensive copy — callers receive an immutable snapshot, not the live ClaimsPrincipal.
        var snapshot = new Dictionary<string, string>();
        foreach (var claim in ctx.User.Claims)
        {
            snapshot[claim.Type] = claim.Value;
        }

        return Task.FromResult<IReadOnlyDictionary<string, string>>(snapshot);
    }
}
