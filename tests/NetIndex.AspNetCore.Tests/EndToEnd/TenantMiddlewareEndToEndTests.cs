using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using Xunit;

namespace NetIndex.AspNetCore.Tests.EndToEnd;

/// <summary>End-to-end integration test wiring the full NetIndex tenant middleware pipeline.</summary>
public class TenantMiddlewareEndToEndTests
{
    /// <summary>
    /// A tenant header present in the request is resolved by HttpContextTenantResolver
    /// and returned as the response body; a missing header results in a 401.
    /// </summary>
    [Fact]
    public async Task EndToEnd_WithWebApplicationFactory_TenantHeader_ResolvesToTenantIdAsync()
    {
        await using var factory = new TenantWebApplicationFactory();
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/tenant");
        request.Headers.Add("X-Tenant-Id", "acme");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("acme");

        var unauthResponse = await client.GetAsync("/tenant");
        unauthResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Prefixed claim headers are forwarded through the middleware into the
    /// claims dictionary returned by <see cref="ITenantResolver.ResolveClaimsAsync"/>.
    /// </summary>
    [Fact]
    public async Task EndToEnd_WithWebApplicationFactory_ClaimsHeaders_ForwardToResolverAsync()
    {
        await using var factory = new TenantWebApplicationFactory();
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/claims");
        request.Headers.Add("X-Tenant-Id", "acme");
        request.Headers.Add("X-NetIndex-Claim-Role", "admin");
        request.Headers.Add("X-NetIndex-Claim-Region", "eu");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("region=eu;role=admin");
    }
}

/// <summary>
/// Partial class placeholder so <c>WebApplicationFactory&lt;Program&gt;</c> can resolve a
/// <c>TEntryPoint</c> in the test assembly. The factory fully overrides
/// <c>CreateHostBuilder</c>, so this type's (non-existent) <c>Main</c> is never invoked —
/// it serves only as the typed assembly anchor for the standard
/// <c>Microsoft.AspNetCore.Mvc.Testing</c> pattern.
/// </summary>
public partial class Program
{
}

internal sealed class TenantWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        // WebApplicationFactory.ConfigureHostBuilder sets the content root to a non-existent
        // path derived from the test assembly name. Override it here (after ConfigureHostBuilder
        // runs) so the last-wins IConfiguration source points to an existing directory.
        builder.UseContentRoot(AppContext.BaseDirectory);
        return base.CreateHost(builder);
    }

    protected override IHostBuilder CreateHostBuilder()
    {
        return new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetIndex(net => net.UseAspNetCoreTenant()).Build();
                });
                web.Configure(app =>
                {
                    app.UseNetIndexTenant();
                    app.UseRouting();
#pragma warning disable ASP0014
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/tenant", async context =>
                        {
                            var resolver = context.RequestServices.GetRequiredService<ITenantResolver>();
                            try
                            {
                                var tenantId = await resolver.ResolveTenantIdAsync(context.RequestAborted);
                                context.Response.StatusCode = 200;
                                await context.Response.WriteAsync(tenantId);
                            }
                            catch (NetIndexAuthorizationException)
                            {
                                context.Response.StatusCode = 401;
                            }
                        });

                        endpoints.MapGet("/claims", async context =>
                        {
                            var resolver = context.RequestServices.GetRequiredService<ITenantResolver>();
                            try
                            {
                                var claims = await resolver.ResolveClaimsAsync(context.RequestAborted);
                                context.Response.StatusCode = 200;
                                var body = string.Join(";", claims.OrderBy(c => c.Key).Select(c => $"{c.Key}={c.Value}"));
                                await context.Response.WriteAsync(body);
                            }
                            catch (NetIndexAuthorizationException)
                            {
                                context.Response.StatusCode = 401;
                            }
                        });
                    });
#pragma warning restore ASP0014
                });
            });
    }
}
