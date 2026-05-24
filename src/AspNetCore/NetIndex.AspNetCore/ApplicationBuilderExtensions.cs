using Microsoft.AspNetCore.Builder;
using NetIndex.AspNetCore.Middleware;

namespace NetIndex.AspNetCore;

/// <summary>
/// Extension methods for adding NetIndex middleware to the ASP.NET Core request pipeline.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the <see cref="NetIndexTenantMiddleware"/> to the request pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same application builder for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
    /// <remarks>
    /// Call this before mapping endpoints so that the tenant header is extracted and stored
    /// in <c>HttpContext.Items</c> before any pipeline operation resolves the tenant.
    /// </remarks>
    public static IApplicationBuilder UseNetIndexTenant(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<NetIndexTenantMiddleware>();
    }
}
