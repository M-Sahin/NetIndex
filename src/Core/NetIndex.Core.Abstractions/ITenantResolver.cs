using System.Threading;
using System.Threading.Tasks;

namespace NetIndex.Core.Abstractions;

/// <summary>
/// Resolves the current tenant identity and authorization claims.
/// </summary>
/// <remarks>
/// Canonical noun #12 (Resolver) in NOUNS.md.
/// 
/// The default implementation (<c>DenyAllTenantResolver</c>) throws
/// <c>NetIndexAuthorizationException</c> on every call — enforcing deny-all by default (FR9).
/// 
/// Implementations read from ASP.NET Core <c>HttpContext</c>, Azure AD tokens,
/// or other identity providers to extract tenant and user claims.
/// </remarks>
public interface ITenantResolver
{
    /// <summary>
    /// Resolves the tenant identifier for the current request context.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tenant ID string.</returns>
    /// <exception cref="System.NotImplementedException">
    /// Thrown when no tenant can be resolved (e.g., missing claims or unauthenticated request).
    /// The concrete <c>NetIndexAuthorizationException</c> type is defined in story 1.3.
    /// </exception>
    Task<string> ResolveTenantIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves all authorization claims for the current request context.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary of claim names to values.</returns>
    /// <exception cref="System.NotImplementedException">
    /// Thrown when no claims can be resolved. The concrete <c>NetIndexAuthorizationException</c>
    /// type is defined in story 1.3.
    /// </exception>
    Task<IReadOnlyDictionary<string, string>> ResolveClaimsAsync(CancellationToken cancellationToken = default);
}
