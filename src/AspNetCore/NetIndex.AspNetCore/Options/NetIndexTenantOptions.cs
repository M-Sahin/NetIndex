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
    /// <para>
    /// A pure-whitespace value is treated as <strong>disabled</strong> — no headers are forwarded.
    /// </para>
    /// </remarks>
    public string ClaimsHeaderPrefix { get; set; } = "X-NetIndex-Claim-";

    /// <summary>
    /// Whether to accept <c>X-NetIndex-Claim-*</c> (or <see cref="ClaimsHeaderPrefix"/>-prefixed)
    /// request headers and forward them as claims.
    /// </summary>
    /// <remarks>
    /// <strong>Defaults to <c>false</c> (secure-by-default).</strong>
    /// Claim-header pass-through is an escalation surface: a client that can forge
    /// <c>X-NetIndex-Claim-Role: admin</c> can impersonate any identity. Enable this only when
    /// all of the following are true:
    /// <list type="number">
    ///   <item>The NetIndex service is placed behind a trusted reverse proxy or API gateway.</item>
    ///   <item>That gateway <strong>strips</strong> inbound <c>X-NetIndex-Claim-*</c> headers
    ///         before forwarding client requests.</item>
    ///   <item>The gateway adds its own claim headers after authenticating the client.</item>
    ///   <item>The gateway IP(s) are listed in <see cref="TrustedProxies"/>.</item>
    /// </list>
    /// Enabling this flag without configuring <see cref="TrustedProxies"/> still rejects
    /// claim headers from every remote address.
    /// </remarks>
    public bool AcceptClaimHeaders { get; set; } = false;

    /// <summary>
    /// IP addresses (exact string match) of reverse proxies or API gateways that are allowed
    /// to forward claim headers when <see cref="AcceptClaimHeaders"/> is <c>true</c>.
    /// </summary>
    /// <remarks>
    /// An empty list means no address is trusted even when <see cref="AcceptClaimHeaders"/> is
    /// <c>true</c>. Supports IPv4 and IPv6 address strings (e.g. <c>"127.0.0.1"</c>, <c>"::1"</c>).
    /// </remarks>
    public IList<string> TrustedProxies { get; } = new List<string>();
}
