using NetIndex.Core.Abstractions;

namespace NetIndex.Core.NullObjects;

/// <summary>
/// Default tenant resolver that enforces deny-all authorization.
/// </summary>
public sealed class DenyAllTenantResolver : ITenantResolver
{
    private const string ErrorMessage = "No ITenantResolver configured. Access denied by default.";

    /// <inheritdoc />
    public Task<string> ResolveTenantIdAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NetIndexAuthorizationException(ErrorMessage, null, null, "NoTenantResolverConfigured");
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> ResolveClaimsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NetIndexAuthorizationException(ErrorMessage, null, null, "NoTenantResolverConfigured");
    }
}
