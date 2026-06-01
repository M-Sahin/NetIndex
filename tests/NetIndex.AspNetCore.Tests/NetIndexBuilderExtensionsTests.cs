using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetIndex.AspNetCore.Options;
using NetIndex.Core.Abstractions;
using NSubstitute;
using Xunit;

namespace NetIndex.AspNetCore.Tests;

/// <summary>Unit tests for <see cref="NetIndexBuilderExtensions"/>.</summary>
public class NetIndexBuilderExtensionsTests
{
    private static INetIndexBuilder CreateBuilder()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);
        return builder;
    }

    /// <summary>UseAspNetCoreTenant with no args registers IHttpContextAccessor and ITenantResolver as HttpContextTenantResolver.</summary>
    [Fact]
    public void UseAspNetCoreTenant_RegistersResolverAndAccessor()
    {
        var builder = CreateBuilder();

        builder.UseAspNetCoreTenant();

        using var provider = builder.Services.BuildServiceProvider();
        provider.GetService<IHttpContextAccessor>().Should().NotBeNull();
        provider.GetService<ITenantResolver>().Should().BeOfType<HttpContextTenantResolver>();
    }

    /// <summary>Pre-registered ITenantResolver is not overridden by UseAspNetCoreTenant (TryAddSingleton semantics).</summary>
    [Fact]
    public void UseAspNetCoreTenant_TryAddSingleton_DoesNotOverridePriorResolver()
    {
        var builder = CreateBuilder();
        var stub = Substitute.For<ITenantResolver>();
        builder.Services.AddSingleton(stub);

        builder.UseAspNetCoreTenant();

        using var provider = builder.Services.BuildServiceProvider();
        provider.GetService<ITenantResolver>().Should().BeSameAs(stub);
    }

    /// <summary>IConfigurationSection overload binds options from configuration.</summary>
    [Fact]
    public void UseAspNetCoreTenant_WithSection_RegistersBindingValidatorAndStore()
    {
        var builder = CreateBuilder();
        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tenant:HeaderName"] = "X-My-Tenant",
                ["Tenant:ClaimsHeaderPrefix"] = "X-Claims-",
            })
            .Build()
            .GetSection("Tenant");

        builder.UseAspNetCoreTenant(section);

        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<NetIndexTenantOptions>>().Value;
        options.HeaderName.Should().Be("X-My-Tenant");
        options.ClaimsHeaderPrefix.Should().Be("X-Claims-");
    }

    /// <summary>Validator rejects null, empty, and whitespace HeaderName values.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UseAspNetCoreTenant_Validator_RejectsBlankHeaderName(string? headerName)
    {
        var validator = new NetIndexTenantOptionsValidator();
        var options = new NetIndexTenantOptions { HeaderName = headerName! };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("HeaderName");
    }

    /// <summary>UseAspNetCoreTenant returns the builder for fluent chaining.</summary>
    [Fact]
    public void UseAspNetCoreTenant_ReturnsBuilderForChaining()
    {
        var builder = CreateBuilder();

        var returned = builder.UseAspNetCoreTenant();

        returned.Should().BeSameAs(builder);
    }

    /// <summary>UseAspNetCoreTenant(Action) with null builder throws ArgumentNullException.</summary>
    [Fact]
    public void UseAspNetCoreTenant_NullBuilder_ThrowsArgumentNullException()
    {
        INetIndexBuilder? builder = null;

        var act = () => builder!.UseAspNetCoreTenant();

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>UseAspNetCoreTenant(IConfigurationSection) with null section throws ArgumentNullException.</summary>
    [Fact]
    public void UseAspNetCoreTenant_WithSection_NullSection_ThrowsArgumentNullException()
    {
        var builder = CreateBuilder();

        var act = () => builder.UseAspNetCoreTenant((IConfigurationSection)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Calling UseAspNetCoreTenant twice registers only one IConfigureOptions delegate (no accumulation)
    /// and the first call's options win — the second call is ignored entirely.
    /// </summary>
    [Fact]
    public void UseAspNetCoreTenant_CalledTwice_DoesNotAccumulateAndFirstCallWins()
    {
        var builder = CreateBuilder();

        builder.UseAspNetCoreTenant(opt => opt.HeaderName = "X-Tenant-First");
        builder.UseAspNetCoreTenant(opt => opt.HeaderName = "X-Tenant-Second");

        var count = builder.Services.Count(d => d.ServiceType == typeof(IConfigureOptions<NetIndexTenantOptions>));
        count.Should().Be(1, "a second call must not add another Configure delegate");

        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<NetIndexTenantOptions>>().Value;
        options.HeaderName.Should().Be("X-Tenant-First", "first call wins; the second call is ignored");
    }
}
