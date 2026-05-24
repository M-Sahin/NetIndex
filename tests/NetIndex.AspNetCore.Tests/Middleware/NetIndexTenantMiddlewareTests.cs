using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NetIndex.AspNetCore.Middleware;
using NetIndex.AspNetCore.Options;
using Xunit;

// Alias to avoid ambiguity with the NetIndex.AspNetCore.Options namespace
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace NetIndex.AspNetCore.Tests.Middleware;

/// <summary>Unit tests for <see cref="NetIndexTenantMiddleware"/>.</summary>
public class NetIndexTenantMiddlewareTests
{
    private static NetIndexTenantMiddleware CreateMiddleware(
        RequestDelegate? next = null,
        Action<NetIndexTenantOptions>? configure = null)
    {
        var options = new NetIndexTenantOptions();
        configure?.Invoke(options);
        return new NetIndexTenantMiddleware(
            next ?? (_ => Task.CompletedTask),
            OptionsFactory.Create(options));
    }

    /// <summary>Middleware populates TenantContextKey when the configured header is present.</summary>
    [Fact]
    public async Task NetIndexTenantMiddleware_PopulatesItems_WhenHeaderPresentAsync()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(next: ctx => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = "acme";

        await middleware.InvokeAsync(context);

        context.Items[NetIndexTenantMiddleware.TenantContextKey].Should().Be("acme");
        nextCalled.Should().BeTrue();
    }

    /// <summary>Middleware does not throw and still calls _next when the header is absent.</summary>
    [Fact]
    public async Task NetIndexTenantMiddleware_DoesNotThrow_WhenHeaderMissingAsync()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(next: ctx => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();

        var act = async () => await middleware.InvokeAsync(context);

        await act.Should().NotThrowAsync();
        context.Items[NetIndexTenantMiddleware.TenantContextKey].Should().BeNull();
        nextCalled.Should().BeTrue();
    }

    /// <summary>Middleware reads from a custom header name when configured.</summary>
    [Fact]
    public async Task NetIndexTenantMiddleware_RespectsCustomHeader_NameAsync()
    {
        var middleware = CreateMiddleware(configure: opts => opts.HeaderName = "X-MyTenant");
        var context = new DefaultHttpContext();
        context.Request.Headers["X-MyTenant"] = "corp";

        await middleware.InvokeAsync(context);

        context.Items[NetIndexTenantMiddleware.TenantContextKey].Should().Be("corp");
    }

    /// <summary>Middleware copies prefixed headers into the claims context dictionary with keys stripped and lowercased.</summary>
    [Fact]
    public async Task NetIndexTenantMiddleware_CopiesPrefixedHeaders_IntoClaimsContextAsync()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-NetIndex-Claim-Role"] = "admin";
        context.Request.Headers["X-NetIndex-Claim-Region"] = "eu";

        await middleware.InvokeAsync(context);

        var claims = context.Items[NetIndexTenantMiddleware.ClaimsContextKey]
            .Should().BeOfType<Dictionary<string, string>>().Subject;
        claims.Should().ContainKey("role").WhoseValue.Should().Be("admin");
        claims.Should().ContainKey("region").WhoseValue.Should().Be("eu");
    }

    /// <summary>Middleware does not set ClaimsContextKey when ClaimsHeaderPrefix is empty.</summary>
    [Fact]
    public async Task NetIndexTenantMiddleware_NoClaimsItem_WhenPrefixIsEmptyAsync()
    {
        var middleware = CreateMiddleware(configure: opts => opts.ClaimsHeaderPrefix = "");
        var context = new DefaultHttpContext();
        context.Request.Headers["X-NetIndex-Claim-Role"] = "admin";

        await middleware.InvokeAsync(context);

        context.Items[NetIndexTenantMiddleware.ClaimsContextKey].Should().BeNull();
    }
}
