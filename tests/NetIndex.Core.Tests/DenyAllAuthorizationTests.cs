using Xunit;
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using NetIndex.Core.NullObjects;
using NSubstitute;
using Microsoft.Extensions.DependencyInjection;

namespace NetIndex.Core.Tests;

/// <summary>
/// Security contract tests for deny-all authorization enforcement (FR9).
/// </summary>
[Trait("Category", "SecurityContract")]
public sealed class DenyAllAuthorizationTests
{
    /// <summary>
    /// Verifies that IngestAsync throws NetIndexAuthorizationException when DenyAllTenantResolver is the default.
    /// </summary>
    [Fact]
    public async Task IngestAsync_WithDenyAllResolver_ThrowsAuthorizationExceptionAsync()
    {
        var services = new ServiceCollection();
        var builder = services.AddNetIndex();
        builder.Build();

        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<INetIndexPipeline>();

        var document = Substitute.For<IDocument>();
        document.Id.Returns("doc-1");
        document.Content.Returns("test");

        var exception = await Assert.ThrowsAsync<NetIndexAuthorizationException>(
            () => pipeline.IngestAsync(document));

        Assert.Equal("No ITenantResolver configured. Access denied by default.", exception.Message);
        Assert.Null(exception.TenantId);
        Assert.Null(exception.RequiredClaim);
        Assert.Equal("NoTenantResolverConfigured", exception.FailureReason);
    }

    /// <summary>
    /// Verifies that QueryAsync throws NetIndexAuthorizationException when a custom resolver that returns null is registered.
    /// </summary>
    [Fact]
    public async Task QueryAsync_WithNullResolver_ThrowsAuthorizationExceptionAsync()
    {
        var services = new ServiceCollection();
        var fakeResolver = Substitute.For<ITenantResolver>();
#pragma warning disable CS8604
        fakeResolver.ResolveTenantIdAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult((string)null!));
#pragma warning restore CS8604
        services.AddSingleton<ITenantResolver>(fakeResolver);

        var builder = services.AddNetIndex();
        builder.Build();

        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<INetIndexPipeline>();

        await Assert.ThrowsAsync<NetIndexAuthorizationException>(
            async () => { await foreach (var _ in pipeline.QueryAsync("test")) { } });
    }

    /// <summary>
    /// Verifies that CancellationToken is propagated to the tenant resolver.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_CancellationPropagatedAsync()
    {
        var services = new ServiceCollection();
        var fakeResolver = Substitute.For<ITenantResolver>();
        fakeResolver.ResolveTenantIdAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult("tenant-456"));
        services.AddSingleton<ITenantResolver>(fakeResolver);

        var builder = services.AddNetIndex();
        builder.Build();

        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<INetIndexPipeline>();
        var cts = new CancellationTokenSource();

        await foreach (var _ in pipeline.GenerateAsync("test", cts.Token)) { }

        await fakeResolver.Received(1).ResolveTenantIdAsync(Arg.Is<CancellationToken>(c => c == cts.Token));
    }

    /// <summary>
    /// Verifies that AddNetIndex() with no configuration results in deny-all behavior.
    /// </summary>
    [Fact]
    public void Build_WithDefaults_DenyAllIsUnconditional()
    {
        var services = new ServiceCollection();
        var builder = services.AddNetIndex();
        _ = builder.Build();

        using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<ITenantResolver>();

        Assert.IsType<DenyAllTenantResolver>(resolver);
    }

    /// <summary>
    /// Verifies that a custom resolver registered via builder callback replaces deny-all.
    /// </summary>
    [Fact]
    public void Build_WithCustomResolver_ReplacesDenyAll()
    {
        var services = new ServiceCollection();
        var customResolver = Substitute.For<ITenantResolver>();
        var builder = services.AddNetIndex(b =>
        {
            b.Services.AddSingleton<ITenantResolver>(customResolver);
        });
        _ = builder.Build();

        using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<ITenantResolver>();

        Assert.Same(customResolver, resolver);
        Assert.IsNotType<DenyAllTenantResolver>(resolver);
    }
}
