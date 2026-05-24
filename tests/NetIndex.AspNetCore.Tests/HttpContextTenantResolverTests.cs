using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NetIndex.AspNetCore.Middleware;
using NetIndex.AspNetCore.Options;
using NetIndex.Core.Abstractions;
using NSubstitute;
using Xunit;

// Alias to avoid ambiguity with the NetIndex.AspNetCore.Options namespace
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace NetIndex.AspNetCore.Tests;

/// <summary>Unit tests for <see cref="HttpContextTenantResolver"/>.</summary>
public class HttpContextTenantResolverTests
{
    private static HttpContextTenantResolver CreateResolver(
        HttpContext? httpContext,
        Action<NetIndexTenantOptions>? configure = null)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);

        var options = new NetIndexTenantOptions();
        configure?.Invoke(options);

        return new HttpContextTenantResolver(accessor, OptionsFactory.Create(options));
    }

    private static DefaultHttpContext ContextWithTenant(string tenantId)
    {
        var ctx = new DefaultHttpContext();
        ctx.Items[NetIndexTenantMiddleware.TenantContextKey] = tenantId;
        return ctx;
    }

    /// <summary>ResolveTenantIdAsync returns the tenant ID stored in HttpContext.Items.</summary>
    [Fact]
    public async Task HttpContextTenantResolver_ReturnsTenantId_FromHttpContextItemsAsync()
    {
        var resolver = CreateResolver(ContextWithTenant("acme"));

        var result = await resolver.ResolveTenantIdAsync();

        result.Should().Be("acme");
    }

    /// <summary>ResolveTenantIdAsync throws NetIndexAuthorizationException with MissingTenantHeader when TenantContextKey is absent.</summary>
    [Fact]
    public async Task HttpContextTenantResolver_ThrowsAuthorization_WhenTenantMissingAsync()
    {
        var resolver = CreateResolver(new DefaultHttpContext());

        var act = async () => await resolver.ResolveTenantIdAsync();

        var ex = await act.Should().ThrowAsync<NetIndexAuthorizationException>();
        ex.Which.FailureReason.Should().Be("MissingTenantHeader");
        ex.Which.RequiredClaim.Should().Be("X-Tenant-Id");
    }

    /// <summary>ResolveTenantIdAsync throws NetIndexAuthorizationException with NoHttpContext when accessor returns null.</summary>
    [Fact]
    public async Task HttpContextTenantResolver_ThrowsAuthorization_WhenNoHttpContextAsync()
    {
        var resolver = CreateResolver(httpContext: null);

        var act = async () => await resolver.ResolveTenantIdAsync();

        var ex = await act.Should().ThrowAsync<NetIndexAuthorizationException>();
        ex.Which.FailureReason.Should().Be("NoHttpContext");
    }

    /// <summary>Both methods throw OperationCanceledException when called with a pre-cancelled token.</summary>
    [Fact]
    public async Task HttpContextTenantResolver_ThrowsOperationCanceled_OnPreCancelledTokenAsync()
    {
        var resolver = CreateResolver(ContextWithTenant("acme"));
        var ct = new CancellationToken(canceled: true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => resolver.ResolveTenantIdAsync(ct));
        await Assert.ThrowsAsync<OperationCanceledException>(() => resolver.ResolveClaimsAsync(ct));
    }

    /// <summary>ResolveClaimsAsync returns the claims dictionary stored in HttpContext.Items.</summary>
    [Fact]
    public async Task HttpContextTenantResolver_ReturnsClaimsDictionary_FromItemsAsync()
    {
        var ctx = new DefaultHttpContext();
        ctx.Items[NetIndexTenantMiddleware.ClaimsContextKey] = new Dictionary<string, string>
        {
            ["role"] = "admin",
            ["region"] = "eu",
        };
        var resolver = CreateResolver(ctx);

        var claims = await resolver.ResolveClaimsAsync();

        claims.Should().ContainKey("role").WhoseValue.Should().Be("admin");
        claims.Should().ContainKey("region").WhoseValue.Should().Be("eu");
    }

    /// <summary>ResolveClaimsAsync returns an empty dictionary when ClaimsContextKey is absent.</summary>
    [Fact]
    public async Task HttpContextTenantResolver_ReturnsEmptyDictionary_WhenClaimsAbsentAsync()
    {
        var resolver = CreateResolver(new DefaultHttpContext());

        var claims = await resolver.ResolveClaimsAsync();

        claims.Should().BeEmpty();
    }

    /// <summary>ResolveClaimsAsync throws NetIndexAuthorizationException with NoHttpContext when accessor returns null.</summary>
    [Fact]
    public async Task HttpContextTenantResolver_ThrowsAuthorization_WhenNoHttpContext_OnClaimsAsync()
    {
        var resolver = CreateResolver(httpContext: null);

        var act = async () => await resolver.ResolveClaimsAsync();

        var ex = await act.Should().ThrowAsync<NetIndexAuthorizationException>();
        ex.Which.FailureReason.Should().Be("NoHttpContext");
    }
}
