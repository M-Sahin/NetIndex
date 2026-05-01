using NetIndex.Core.Abstractions;

namespace NetIndex.Core;

/// <summary>
/// Minimal pipeline shell registered by <c>AddNetIndex()</c> in Story 2.1.
/// </summary>
public sealed class NetIndexPipeline
{
    private readonly ITenantResolver _tenantResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="NetIndexPipeline"/> class.
    /// </summary>
    /// <param name="tenantResolver">Tenant resolver for authorization checks.</param>
    public NetIndexPipeline(ITenantResolver tenantResolver)
    {
        _tenantResolver = tenantResolver ?? throw new ArgumentNullException(nameof(tenantResolver));
    }

    /// <summary>
    /// Authorizes the current request by resolving the tenant ID.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved tenant ID if authorization succeeds.</returns>
    /// <exception cref="NetIndexAuthorizationException">Thrown when authorization fails.</exception>
    public async Task<string> AuthorizeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _tenantResolver.ResolveTenantIdAsync(cancellationToken);
        }
        catch (NetIndexAuthorizationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new NetIndexAuthorizationException(
                "Authorization failed during tenant resolution.",
                null, null, "TenantResolutionFailed", exception);
        }
    }
}
