namespace NetIndex.AspNetCore.Options;

/// <summary>
/// Configuration options for NetIndex tenant middleware and resolver.
/// </summary>
public sealed class NetIndexTenantOptions
{
    /// <summary>
    /// The HTTP request header that carries the tenant identifier.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>"X-Tenant-Id"</c>. Configure your API gateway or client to forward
    /// this header on every request that touches the RAG pipeline.
    /// </remarks>
    public string HeaderName { get; set; } = "X-Tenant-Id";

    /// <summary>
    /// The prefix used to identify HTTP headers that should be forwarded as authorization claims.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>"X-NetIndex-Claim-"</c>. Any request header whose name starts with this
    /// prefix (case-insensitive) is copied into the tenant claims dictionary with the prefix
    /// stripped and the key lowercased. Set to an empty string to disable claim forwarding.
    /// This is a header pass-through mechanism for upstream-already-authenticated requests
    /// (e.g., an API gateway that translates JWT claims into request headers before forwarding);
    /// it is not a replacement for <c>ClaimsTenantResolver</c> (Story 6.1).
    /// </remarks>
    public string ClaimsHeaderPrefix { get; set; } = "X-NetIndex-Claim-";
}
