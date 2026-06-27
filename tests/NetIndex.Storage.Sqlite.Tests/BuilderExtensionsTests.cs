using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Storage.Sqlite;
using NetIndex.Storage.Sqlite.Options;
using NSubstitute;

namespace NetIndex.Storage.Sqlite.Tests;

/// <summary>Unit tests for <see cref="NetIndexBuilderExtensions"/>.</summary>
public class BuilderExtensionsTests
{
    private static INetIndexBuilder CreateBuilder()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<INetIndexBuilder>();
        builder.Services.Returns(services);
        return builder;
    }

    /// <summary>UseSqlite(string) with null builder throws ArgumentNullException.</summary>
    [Fact]
    public void UseSqlite_WithConnectionString_NullBuilder_ThrowsArgumentNullException()
    {
        var act = () => ((INetIndexBuilder)null!).UseSqlite("Data Source=./rag.db");
        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    /// <summary>UseSqlite(Action) with null builder throws ArgumentNullException.</summary>
    [Fact]
    public void UseSqlite_WithDelegate_NullBuilder_ThrowsArgumentNullException()
    {
        var act = () => ((INetIndexBuilder)null!).UseSqlite();
        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    /// <summary>UseSqlite(string) with null or whitespace connection string throws ArgumentException.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UseSqlite_WithNullOrWhitespaceConnectionString_ThrowsArgumentException(string? connectionString)
    {
        var builder = CreateBuilder();
        var act = () => builder.UseSqlite(connectionString!);
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>UseSqlite(string) registers IVectorStore as SqliteVectorStore.</summary>
    [Fact]
    public void UseSqlite_WithConnectionString_RegistersIVectorStore()
    {
        var builder = CreateBuilder();

        builder.UseSqlite("Data Source=:memory:");

        var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<IVectorStore>().Should().BeOfType<SqliteVectorStore>();
    }

    /// <summary>UseSqlite(Action) registers IVectorStore as SqliteVectorStore.</summary>
    [Fact]
    public void UseSqlite_WithOptionsDelegate_RegistersIVectorStore()
    {
        var builder = CreateBuilder();

        builder.UseSqlite(opts =>
        {
            opts.ConnectionString = "Data Source=:memory:";
            opts.Dimensions = 8;
        });

        var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<IVectorStore>().Should().BeOfType<SqliteVectorStore>();
    }

    /// <summary>Calling UseSqlite twice does not register a duplicate IVectorStore (TryAddSingleton).</summary>
    [Fact]
    public void UseSqlite_CalledTwice_DoesNotRegisterDuplicate()
    {
        var builder = CreateBuilder();

        builder.UseSqlite("Data Source=:memory:");
        builder.UseSqlite("Data Source=:memory:");

        var registrations = builder.Services
            .Where(sd => sd.ServiceType == typeof(IVectorStore))
            .ToList();

        registrations.Should().HaveCount(1);
    }

    /// <summary>UseSqlite returns the same builder for fluent chaining.</summary>
    [Fact]
    public void UseSqlite_ReturnsBuilderForChaining()
    {
        var builder = CreateBuilder();

        var result = builder.UseSqlite("Data Source=:memory:");

        result.Should().BeSameAs(builder);
    }

    /// <summary>
    /// UseSqlite(IConfigurationSection) binds options and registers IVectorStore.
    /// Guards the template's documented swap line <c>UseSqlite(builder.Configuration.GetSection("NetIndex:Sqlite"))</c>.
    /// </summary>
    [Fact]
    public void UseSqlite_WithConfigurationSection_BindsOptionsAndRegistersIVectorStore()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NetIndex:Sqlite:ConnectionString"] = "Data Source=./from-config.db",
                ["NetIndex:Sqlite:Dimensions"] = "8",
            })
            .Build();
        var builder = CreateBuilder();

        builder.UseSqlite(config.GetSection("NetIndex:Sqlite"));

        var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<IVectorStore>().Should().BeOfType<SqliteVectorStore>();
        var resolved = provider.GetRequiredService<IOptions<SqliteOptions>>().Value;
        resolved.ConnectionString.Should().Be("Data Source=./from-config.db");
        resolved.Dimensions.Should().Be(8);
    }

    /// <summary>UseSqlite(IConfigurationSection) with a null section throws ArgumentNullException.</summary>
    [Fact]
    public void UseSqlite_WithNullConfigurationSection_ThrowsArgumentNullException()
    {
        var builder = CreateBuilder();
        var act = () => builder.UseSqlite((IConfigurationSection)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("section");
    }

    /// <summary>The explicit connectionString argument wins over an attempt to override it via the configure delegate.</summary>
    [Fact]
    public void UseSqlite_ExplicitConnectionString_TakesPrecedenceOverConfigureDelegate()
    {
        var builder = CreateBuilder();

        builder.UseSqlite(
            "Data Source=./explicit.db",
            opts =>
            {
                opts.ConnectionString = "Data Source=./from-delegate.db";
                opts.Dimensions = 16;
            });

        var provider = builder.Services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IOptions<SqliteOptions>>().Value;

        resolved.ConnectionString.Should().Be("Data Source=./explicit.db");
        resolved.Dimensions.Should().Be(16);
    }
}
