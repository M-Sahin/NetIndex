#pragma warning disable CS1591
using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NetIndex.AspNetCore.Middleware;
using NetIndex.AspNetCore.Options;
using NetIndex.Core.Abstractions;
using Xunit;

using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace NetIndex.AspNetCore.Tests;

/// <summary>
/// Security-contract tests for middleware trust boundary, claims immutability, and resolver
/// behaviour (Story 6.1, Bucket-A AC-1 through AC-5 and AC-2).
/// All tests are deterministic and require no external infrastructure.
/// </summary>
[Trait("Category", "SecurityContract")]
public sealed class TenantSecurityContractTests
{
    // ────────────────────────────────────────────────────────────────────
    // AC-1: Claim-header trust boundary — secure-by-default
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClaimHeaders_AreIgnored_WhenAcceptClaimHeadersIsFalseByDefaultAsync()
    {
        var middleware = CreateMiddleware(); // AcceptClaimHeaders = false by default
        var ctx = MakeContext(remoteIp: IPAddress.Loopback);
        ctx.Request.Headers["X-NetIndex-Claim-Role"] = "admin";

        await middleware.InvokeAsync(ctx);

        ctx.Items[NetIndexTenantMiddleware.ClaimsContextKey].Should().BeNull();
    }

    [Fact]
    public async Task ClaimHeaders_AreIgnored_WhenOptInButNoTrustedProxiesConfiguredAsync()
    {
        var middleware = CreateMiddleware(opts =>
        {
            opts.AcceptClaimHeaders = true;
            // TrustedProxies intentionally empty
        });
        var ctx = MakeContext(remoteIp: IPAddress.Loopback);
        ctx.Request.Headers["X-NetIndex-Claim-Role"] = "admin";

        await middleware.InvokeAsync(ctx);

        ctx.Items[NetIndexTenantMiddleware.ClaimsContextKey].Should().BeNull();
    }

    [Fact]
    public async Task ClaimHeaders_AreForwarded_WhenOptInAndRequestIsFromTrustedProxyAsync()
    {
        var middleware = CreateMiddleware(opts =>
        {
            opts.AcceptClaimHeaders = true;
            opts.TrustedProxies.Add("127.0.0.1");
        });
        var ctx = MakeContext(remoteIp: IPAddress.Loopback);
        ctx.Request.Headers["X-NetIndex-Claim-Role"] = "admin";

        await middleware.InvokeAsync(ctx);

        ctx.Items[NetIndexTenantMiddleware.ClaimsContextKey].Should().BeOfType<Dictionary<string, string>>()
            .Which.Should().ContainKey("role").WhoseValue.Should().Be("admin");
    }

    // ────────────────────────────────────────────────────────────────────
    // AC-2: Claims immutability / defensive-copy contract
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HttpContextTenantResolver_ReturnsDefensiveCopy_NotLiveDictionaryAsync()
    {
        var middleware = CreateMiddleware(opts =>
        {
            opts.AcceptClaimHeaders = true;
            opts.TrustedProxies.Add("127.0.0.1");
        });
        var ctx = MakeContext(remoteIp: IPAddress.Loopback);
        ctx.Request.Headers["X-Tenant-Id"] = "acme";
        ctx.Request.Headers["X-NetIndex-Claim-Role"] = "admin";
        await middleware.InvokeAsync(ctx);

        var resolver = new HttpContextTenantResolver(
            new StaticHttpContextAccessor(ctx),
            OptionsFactory.Create(new NetIndexTenantOptions { AcceptClaimHeaders = true }));

        var claims1 = await resolver.ResolveClaimsAsync();
        var claims2 = await resolver.ResolveClaimsAsync();

        // Each call returns a new copy — mutations to one do not affect the other.
        claims1.Should().NotBeSameAs(claims2);
        claims1.Should().BeEquivalentTo(claims2);
    }

    // ────────────────────────────────────────────────────────────────────
    // AC-3: Claim-key case-collision detection
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClaimHeaders_CaseVariantCollision_RecordsMalformedMarkerAsync()
    {
        var middleware = CreateMiddleware(opts =>
        {
            opts.AcceptClaimHeaders = true;
            opts.TrustedProxies.Add("127.0.0.1");
        });
        var ctx = MakeContext(remoteIp: IPAddress.Loopback);
        // HTTP coalesces X-NetIndex-Claim-Role and X-NetIndex-Claim-ROLE into one multi-value header.
        ctx.Request.Headers["X-NetIndex-Claim-Role"] =
            new Microsoft.Extensions.Primitives.StringValues(["admin", "evil"]);

        await middleware.InvokeAsync(ctx);

        ctx.Items[NetIndexTenantMiddleware.MalformedTenantKey].Should().Be("ClaimKeyCollision");
        ctx.Items[NetIndexTenantMiddleware.ClaimsContextKey].Should().BeNull();
    }

    [Fact]
    public async Task HttpContextTenantResolver_ThrowsAuthException_WhenClaimKeyCollisionMarkerSetAsync()
    {
        var ctx = MakeContext();
        ctx.Items[NetIndexTenantMiddleware.TenantContextKey] = "acme";
        ctx.Items[NetIndexTenantMiddleware.MalformedTenantKey] = "ClaimKeyCollision";

        var resolver = new HttpContextTenantResolver(
            new StaticHttpContextAccessor(ctx),
            OptionsFactory.Create(new NetIndexTenantOptions()));

        var ex = await Assert.ThrowsAsync<NetIndexAuthorizationException>(
            () => resolver.ResolveTenantIdAsync());

        ex.FailureReason.Should().Be("ClaimKeyCollision");
    }

    // ────────────────────────────────────────────────────────────────────
    // AC-5: Multi-value tenant header → rejected as malformed
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MultiValueTenantHeader_RecordsMalformedMarker_NotSilentlyCommaJoinedAsync()
    {
        var middleware = CreateMiddleware();
        var ctx = MakeContext();
        ctx.Request.Headers["X-Tenant-Id"] =
            new Microsoft.Extensions.Primitives.StringValues(["acme", "evil"]);

        await middleware.InvokeAsync(ctx);

        ctx.Items[NetIndexTenantMiddleware.MalformedTenantKey].Should().Be("MultiValueTenantHeader");
        ctx.Items[NetIndexTenantMiddleware.TenantContextKey].Should().BeNull();
    }

    [Fact]
    public async Task HttpContextTenantResolver_ThrowsAuthException_WhenMultiValueTenantMarkerSetAsync()
    {
        var ctx = MakeContext();
        ctx.Items[NetIndexTenantMiddleware.MalformedTenantKey] = "MultiValueTenantHeader";

        var resolver = new HttpContextTenantResolver(
            new StaticHttpContextAccessor(ctx),
            OptionsFactory.Create(new NetIndexTenantOptions()));

        var ex = await Assert.ThrowsAsync<NetIndexAuthorizationException>(
            () => resolver.ResolveTenantIdAsync());

        ex.FailureReason.Should().Be("MultiValueTenantHeader");
    }

    [Fact]
    public async Task SingleTenantHeaderValue_IsTrimmed_BeforeStoringAsync()
    {
        var middleware = CreateMiddleware();
        var ctx = MakeContext();
        ctx.Request.Headers["X-Tenant-Id"] = "  acme  ";

        await middleware.InvokeAsync(ctx);

        ctx.Items[NetIndexTenantMiddleware.TenantContextKey].Should().Be("acme");
        ctx.Items[NetIndexTenantMiddleware.MalformedTenantKey].Should().BeNull();
    }

    // ────────────────────────────────────────────────────────────────────
    // ClaimsTenantResolver unit tests (AC-Core-3, AC-2)
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClaimsTenantResolver_ReturnsDefensiveCopy_OfClaimsAsync()
    {
        var ctx = MakeAuthenticatedContext("corp",
            new Claim("tenant_id", "corp"),
            new Claim("role", "admin"));

        var resolver = new ClaimsTenantResolver(
            new StaticHttpContextAccessor(ctx),
            OptionsFactory.Create(new ClaimsTenantOptions()));

        var claims1 = await resolver.ResolveClaimsAsync();
        var claims2 = await resolver.ResolveClaimsAsync();

        claims1.Should().NotBeSameAs(claims2);
        claims1.Should().BeEquivalentTo(claims2);
    }

    [Fact]
    public async Task ClaimsTenantResolver_ThrowsMissingClaim_WhenNoClaim_FoundAsync()
    {
        // Authenticated but no tenant_id claim — use an identity with only a role claim
        var ctx = MakeContext();
        ctx.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("role", "user")], authenticationType: "test"));

        var resolver = new ClaimsTenantResolver(
            new StaticHttpContextAccessor(ctx),
            OptionsFactory.Create(new ClaimsTenantOptions()));

        var ex = await Assert.ThrowsAsync<NetIndexAuthorizationException>(
            () => resolver.ResolveTenantIdAsync());

        ex.FailureReason.Should().Be("MissingTenantIdClaim");
    }

    [Fact]
    public async Task ClaimsTenantResolver_ThrowsUnauthenticated_WhenIdentityIsNotAuthenticatedAsync()
    {
        var ctx = MakeContext(); // anonymous, not authenticated
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity()); // not authenticated

        var resolver = new ClaimsTenantResolver(
            new StaticHttpContextAccessor(ctx),
            OptionsFactory.Create(new ClaimsTenantOptions()));

        var ex = await Assert.ThrowsAsync<NetIndexAuthorizationException>(
            () => resolver.ResolveTenantIdAsync());

        ex.FailureReason.Should().Be("Unauthenticated");
    }

    [Fact]
    public async Task ClaimsTenantResolver_ThrowsNoHttpContext_WhenContextIsNullAsync()
    {
        var resolver = new ClaimsTenantResolver(
            new StaticHttpContextAccessor(null),
            OptionsFactory.Create(new ClaimsTenantOptions()));

        var ex = await Assert.ThrowsAsync<NetIndexAuthorizationException>(
            () => resolver.ResolveTenantIdAsync());

        ex.FailureReason.Should().Be("NoHttpContext");
    }

    // ────────────────────────────────────────────────────────────────────
    // Validator: AC-4 — RFC 7230 token grammar
    // ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(" X-Tenant-Id ")]    // padded whitespace
    [InlineData("X Tenant Id")]      // space inside
    [InlineData("X\r\nInjected: y")] // CRLF injection
    public void Validator_RejectsInvalidHeaderName(string invalidName)
    {
        var validator = new NetIndexTenantOptionsValidatorAccessor();
        var result = validator.ValidatePublic(null, new NetIndexTenantOptions { HeaderName = invalidName });
        result.Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData(" X-Claim- ")]   // padded whitespace
    [InlineData("X Claim ")]     // space
    [InlineData("X\r\nFoo: x")] // CRLF injection
    public void Validator_RejectsInvalidClaimsHeaderPrefix(string invalidPrefix)
    {
        var validator = new NetIndexTenantOptionsValidatorAccessor();
        var result = validator.ValidatePublic(null, new NetIndexTenantOptions { ClaimsHeaderPrefix = invalidPrefix });
        result.Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]   // empty → disabled (no validation)
    [InlineData(" ")]  // whitespace → disabled
    [InlineData("   ")] // multiple spaces → disabled
    public void Validator_AcceptsWhitespaceOrEmptyClaimsHeaderPrefix_AsDisabled(string disabledPrefix)
    {
        var validator = new NetIndexTenantOptionsValidatorAccessor();
        var result = validator.ValidatePublic(null, new NetIndexTenantOptions { ClaimsHeaderPrefix = disabledPrefix });
        result.Failed.Should().BeFalse();
    }

    [Fact]
    public void Validator_AcceptsValidDefaultOptions()
    {
        var validator = new NetIndexTenantOptionsValidatorAccessor();
        var result = validator.ValidatePublic(null, new NetIndexTenantOptions());
        result.Failed.Should().BeFalse();
    }

    // ────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────

    private static NetIndexTenantMiddleware CreateMiddleware(Action<NetIndexTenantOptions>? configure = null)
    {
        var options = new NetIndexTenantOptions();
        configure?.Invoke(options);
        return new NetIndexTenantMiddleware(
            _ => Task.CompletedTask,
            OptionsFactory.Create(options));
    }

    private static DefaultHttpContext MakeContext(IPAddress? remoteIp = null)
    {
        var ctx = new DefaultHttpContext();
        if (remoteIp is not null)
        {
            ctx.Connection.RemoteIpAddress = remoteIp;
        }
        return ctx;
    }

    private static DefaultHttpContext MakeAuthenticatedContext(string tenantId, params Claim[] additionalClaims)
    {
        var ctx = MakeContext();
        var claims = new List<Claim>(additionalClaims) { new Claim("tenant_id", tenantId) };
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
        return ctx;
    }

    /// <summary>
    /// Exposes the internal Validate method of <see cref="NetIndexTenantOptionsValidator"/>
    /// for testing without requiring InternalsVisibleTo.
    /// </summary>
    private sealed class NetIndexTenantOptionsValidatorAccessor
    {
        private readonly IValidateOptions<NetIndexTenantOptions> _inner =
            new NetIndexTenantOptionsValidator();

        public ValidateOptionsResult ValidatePublic(string? name, NetIndexTenantOptions options)
            => _inner.Validate(name, options);
    }

    private sealed class StaticHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }

        public StaticHttpContextAccessor(HttpContext? context) => HttpContext = context;
    }
}
