using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using NetIndex.Core.Options;
using Xunit;

namespace NetIndex.Core.Tests;

internal interface ITestFeatureMarker
{
}

internal sealed class TestFeatureMarker : ITestFeatureMarker
{
}

internal static class TestNetIndexBuilderExtensions
{
    public static INetIndexBuilder UseTestFeature(this INetIndexBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<ITestFeatureMarker, TestFeatureMarker>();
        return builder;
    }
}

/// <summary>
/// Contract tests for NetIndex core service registration.
/// </summary>
[Trait("Category", "PipelineContract")]
public sealed class ServiceCollectionExtensionsTests
{
    /// <summary>
    /// Verifies that AddNetIndex returns a fluent builder without additional configuration.
    /// </summary>
    [Fact]
    public void AddNetIndex_WithNoConfiguration_ReturnsBuilder()
    {
        var services = new ServiceCollection();

        var builder = services.AddNetIndex();

        Assert.IsAssignableFrom<INetIndexBuilder>(builder);
    }

    /// <summary>
    /// Verifies that the optional configure callback is invoked exactly once.
    /// </summary>
    [Fact]
    public void AddNetIndex_WithConfigureCallback_InvokesCallback()
    {
        var services = new ServiceCollection();
        var configureCalled = false;

        var builder = services.AddNetIndex(configure =>
        {
            configureCalled = true;
            Assert.NotNull(configure);
            configure.UseTestFeature();
        });

        Assert.True(configureCalled);
        Assert.IsAssignableFrom<INetIndexBuilder>(builder);
    }

    /// <summary>
    /// Verifies that builder extensions can chain through the public registration hook.
    /// </summary>
    [Fact]
    public void AddNetIndex_WithUseFeatureExtension_RegistersFeatureServices()
    {
        var services = new ServiceCollection();
        var builder = services.AddNetIndex(configure => configure.UseTestFeature());

        _ = builder.Build();

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<ITestFeatureMarker>());
    }

    /// <summary>
    /// Verifies that Build wires the default core services into DI.
    /// </summary>
    [Fact]
    public void Build_WithDefaults_RegistersCoreServices()
    {
        var services = new ServiceCollection();
        var builder = services.AddNetIndex();

        var pipeline = builder.Build();

        Assert.NotNull(pipeline);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<ITenantResolver>());
        Assert.NotNull(provider.GetService<IVectorStore>());
        Assert.NotNull(provider.GetService<IEmbeddingGenerator>());
        Assert.NotNull(provider.GetService<IChatClient>());
        Assert.NotNull(provider.GetService<NetIndexPipeline>());
    }

    /// <summary>
    /// Verifies that the default tenant resolver enforces deny-all behavior.
    /// </summary>
    [Fact]
    public async Task Build_WithDefaults_UsesDenyAllTenantResolverAsync()
    {
        var services = new ServiceCollection();
        var builder = services.AddNetIndex();
        _ = builder.Build();

        using var provider = services.BuildServiceProvider();
        var tenantResolver = provider.GetRequiredService<ITenantResolver>();

        var tenantException = await Assert.ThrowsAsync<NetIndexAuthorizationException>(
            () => tenantResolver.ResolveTenantIdAsync());

        Assert.Equal("No ITenantResolver configured. Access denied by default.", tenantException.Message);

        var claimsException = await Assert.ThrowsAsync<NetIndexAuthorizationException>(
            () => tenantResolver.ResolveClaimsAsync());

        Assert.Equal("No ITenantResolver configured. Access denied by default.", claimsException.Message);
    }

    /// <summary>
    /// Verifies that AddNetIndex registers options validation services.
    /// </summary>
    [Fact]
    public void AddNetIndex_RegistersNetIndexOptionsValidation()
    {
        var services = new ServiceCollection();
        _ = services.AddNetIndex();

        using var provider = services.BuildServiceProvider();
        var validators = provider.GetServices<IValidateOptions<NetIndexOptions>>();

        Assert.NotEmpty(validators);
    }
}
