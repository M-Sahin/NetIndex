using NetIndex.Core.Abstractions;

namespace NetIndex.Template;

/// <summary>
/// Development-only tenant resolver that allows all pipeline operations to proceed
/// without real authentication. Returns a fixed tenant ID of <c>"local-dev"</c>.
/// </summary>
/// <remarks>
/// <strong>⚠ DEV ONLY — Remove or replace before serving production traffic.</strong>
/// <br/>
/// This resolver exists solely to satisfy the deny-all default enforced by
/// <c>DenyAllTenantResolver</c>. In production, configure a real
/// <see cref="ITenantResolver"/> (for example, <c>ClaimsTenantResolver</c> from
/// <c>NetIndex.AspNetCore</c>) that validates JWTs, Azure AD tokens, or API keys.
/// </remarks>
internal sealed class LocalDevTenantResolver : ITenantResolver
{
    private const string DevTenantId = "local-dev";

    private static readonly IReadOnlyDictionary<string, string> Claims = new Dictionary<string, string>
    {
        ["tenant_id"] = DevTenantId,
    };

    /// <inheritdoc />
    public Task<string> ResolveTenantIdAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DevTenantId);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> ResolveClaimsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Claims);
    }
}
