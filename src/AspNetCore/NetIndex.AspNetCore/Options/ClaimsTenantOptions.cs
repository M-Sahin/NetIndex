namespace NetIndex.AspNetCore.Options;

/// <summary>
/// Configuration options for <see cref="ClaimsTenantResolver"/>.
/// </summary>
public sealed class ClaimsTenantOptions
{
    /// <summary>
    /// The claim type whose value is used as the tenant identifier.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>"tenant_id"</c>. The resolved <see cref="System.Security.Claims.ClaimsPrincipal"/>
    /// must carry a claim of this type for tenant resolution to succeed.
    /// </remarks>
    public string TenantClaimType { get; set; } = "tenant_id";
}
