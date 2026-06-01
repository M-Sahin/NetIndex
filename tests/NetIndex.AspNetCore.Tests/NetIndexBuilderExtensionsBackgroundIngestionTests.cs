using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NetIndex.AspNetCore.BackgroundServices;
using NetIndex.AspNetCore.Options;
using NetIndex.Core.Abstractions;
using NSubstitute;
using Xunit;

namespace NetIndex.AspNetCore.Tests;

/// <summary>Unit tests for the <c>UseBackgroundIngestion</c> extensions on <see cref="NetIndexBuilderExtensions"/>.</summary>
public class NetIndexBuilderExtensionsBackgroundIngestionTests
{
    private static INetIndexBuilder CreateBuilder()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);
        return builder;
    }

    /// <summary>UseBackgroundIngestion registers the channel queue and the hosted service.</summary>
    [Fact]
    public void UseBackgroundIngestion_RegistersQueueAndHostedService()
    {
        var builder = CreateBuilder();

        builder.UseBackgroundIngestion();

        builder.Services.Should().Contain(d =>
            d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(IngestionHostedService));

        using var provider = builder.Services.BuildServiceProvider();
        provider.GetService<IIngestionQueue>().Should().BeOfType<ChannelIngestionQueue>();
    }

    /// <summary>A pre-registered IIngestionQueue is not overridden (TryAddSingleton semantics).</summary>
    [Fact]
    public void UseBackgroundIngestion_TryAddSingleton_DoesNotOverridePriorQueue()
    {
        var builder = CreateBuilder();
        var stub = Substitute.For<IIngestionQueue>();
        builder.Services.AddSingleton(stub);

        builder.UseBackgroundIngestion();

        using var provider = builder.Services.BuildServiceProvider();
        provider.GetService<IIngestionQueue>().Should().BeSameAs(stub);
    }

    /// <summary>IConfigurationSection overload binds options from configuration.</summary>
    [Fact]
    public void UseBackgroundIngestion_WithSection_RegistersBindingValidatorAndQueue()
    {
        var builder = CreateBuilder();
        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BackgroundIngestion:QueueCapacity"] = "50",
            })
            .Build()
            .GetSection("BackgroundIngestion");

        builder.UseBackgroundIngestion(section);

        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BackgroundIngestionOptions>>().Value;
        options.QueueCapacity.Should().Be(50);
    }

    /// <summary>UseBackgroundIngestion returns the builder for fluent chaining.</summary>
    [Fact]
    public void UseBackgroundIngestion_ReturnsBuilderForChaining()
    {
        var builder = CreateBuilder();

        var returned = builder.UseBackgroundIngestion();

        returned.Should().BeSameAs(builder);
    }

    /// <summary>UseBackgroundIngestion(Action) with null builder throws ArgumentNullException.</summary>
    [Fact]
    public void UseBackgroundIngestion_NullBuilder_ThrowsArgumentNullException()
    {
        INetIndexBuilder? builder = null;

        var act = () => builder!.UseBackgroundIngestion();

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>UseBackgroundIngestion(IConfigurationSection) with null section throws ArgumentNullException.</summary>
    [Fact]
    public void UseBackgroundIngestion_WithSection_NullSection_ThrowsArgumentNullException()
    {
        var builder = CreateBuilder();

        var act = () => builder.UseBackgroundIngestion((IConfigurationSection)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Calling UseBackgroundIngestion twice registers only one IConfigureOptions delegate (no accumulation)
    /// and the first call's options win — the second call is ignored entirely.
    /// </summary>
    [Fact]
    public void UseBackgroundIngestion_CalledTwice_DoesNotAccumulateAndFirstCallWins()
    {
        var builder = CreateBuilder();

        builder.UseBackgroundIngestion(opt => opt.QueueCapacity = 50);
        builder.UseBackgroundIngestion(opt => opt.QueueCapacity = 100);

        var count = builder.Services.Count(d => d.ServiceType == typeof(IConfigureOptions<BackgroundIngestionOptions>));
        count.Should().Be(1, "a second call must not add another Configure delegate");

        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BackgroundIngestionOptions>>().Value;
        options.QueueCapacity.Should().Be(50, "first call wins; the second call is ignored");
    }
}
