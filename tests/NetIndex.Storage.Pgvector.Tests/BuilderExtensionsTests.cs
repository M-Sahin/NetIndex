using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Storage.Pgvector.Options;
using NSubstitute;
using Xunit;

namespace NetIndex.Storage.Pgvector.Tests;

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

    /// <summary>UsePgvector(string) with null builder throws ArgumentNullException.</summary>
    [Fact]
    public void UsePgvector_WithConnectionString_NullBuilder_ThrowsArgumentNullException()
    {
        var act = () => ((INetIndexBuilder)null!).UsePgvector("Host=localhost;Database=rag");
        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    /// <summary>UsePgvector(Action) with null builder throws ArgumentNullException.</summary>
    [Fact]
    public void UsePgvector_WithDelegate_NullBuilder_ThrowsArgumentNullException()
    {
        var act = () => ((INetIndexBuilder)null!).UsePgvector();
        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    /// <summary>UsePgvector(IConfigurationSection) with null builder throws ArgumentNullException.</summary>
    [Fact]
    public void UsePgvector_WithSection_NullBuilder_ThrowsArgumentNullException()
    {
        var section = Substitute.For<IConfigurationSection>();
        var act = () => ((INetIndexBuilder)null!).UsePgvector(section);
        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    /// <summary>UsePgvector(IConfigurationSection) with null section throws ArgumentNullException.</summary>
    [Fact]
    public void UsePgvector_WithSection_NullSection_ThrowsArgumentNullException()
    {
        var builder = CreateBuilder();
        var act = () => builder.UsePgvector((IConfigurationSection)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("section");
    }

    /// <summary>UsePgvector(string) with null or whitespace connection string throws ArgumentException.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UsePgvector_WithNullOrWhitespaceConnectionString_ThrowsArgumentException(string? connectionString)
    {
        var builder = CreateBuilder();
        var act = () => builder.UsePgvector(connectionString!);
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>UsePgvector(string) registers IVectorStore as PgvectorVectorStore.</summary>
    [Fact]
    public void UsePgvector_WithConnectionString_RegistersIVectorStore()
    {
        var builder = CreateBuilder();

        builder.UsePgvector("Host=localhost;Database=rag;Username=u;Password=p");

        var descriptor = builder.Services.FirstOrDefault(sd => sd.ServiceType == typeof(IVectorStore));
        descriptor.Should().NotBeNull();
        descriptor!.ImplementationType.Should().Be(typeof(PgvectorVectorStore));
    }

    /// <summary>UsePgvector(Action) registers IVectorStore as PgvectorVectorStore.</summary>
    [Fact]
    public void UsePgvector_WithDelegate_RegistersIVectorStore()
    {
        var builder = CreateBuilder();

        builder.UsePgvector(opts =>
        {
            opts.ConnectionString = "Host=localhost;Database=rag";
            opts.Dimensions = 8;
        });

        var descriptor = builder.Services.FirstOrDefault(sd => sd.ServiceType == typeof(IVectorStore));
        descriptor.Should().NotBeNull();
        descriptor!.ImplementationType.Should().Be(typeof(PgvectorVectorStore));
    }

    /// <summary>
    /// Calling UsePgvector twice does not register a duplicate IVectorStore (TryAddSingleton).
    /// Note: each call appends a Configure action, so the second call's settings last-win;
    /// this test only verifies the registration count, not which configuration wins.
    /// </summary>
    [Fact]
    public void UsePgvector_CalledTwice_DoesNotRegisterDuplicate()
    {
        var builder = CreateBuilder();

        builder.UsePgvector("Host=localhost;Database=rag");
        builder.UsePgvector("Host=localhost;Database=rag");

        var registrations = builder.Services
            .Where(sd => sd.ServiceType == typeof(IVectorStore))
            .ToList();

        registrations.Should().HaveCount(1);
        registrations[0].ImplementationType.Should().Be(typeof(PgvectorVectorStore));
    }

    /// <summary>Second UsePgvector call's connection string last-wins because Configure delegates accumulate.</summary>
    [Fact]
    public void UsePgvector_CalledTwice_SecondConnectionStringWins()
    {
        var builder = CreateBuilder();

        builder.UsePgvector("Host=first;Database=rag");
        builder.UsePgvector("Host=second;Database=rag");

        var provider = builder.Services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IOptions<PgvectorOptions>>().Value;

        resolved.ConnectionString.Should().Be("Host=second;Database=rag");
    }

    /// <summary>UsePgvector returns the same builder for fluent chaining.</summary>
    [Fact]
    public void UsePgvector_ReturnsBuilderForChaining()
    {
        var builder = CreateBuilder();

        var result = builder.UsePgvector("Host=localhost;Database=rag");

        result.Should().BeSameAs(builder);
    }

    /// <summary>The explicit connectionString argument wins over the configure delegate.</summary>
    [Fact]
    public void UsePgvector_ConnectionStringOverload_WinsOverConfigure()
    {
        var builder = CreateBuilder();

        builder.UsePgvector(
            "Host=explicit;Database=rag",
            opts =>
            {
                opts.ConnectionString = "Host=from-delegate;Database=rag";
                opts.Dimensions = 16;
            });

        var provider = builder.Services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IOptions<PgvectorOptions>>().Value;

        resolved.ConnectionString.Should().Be("Host=explicit;Database=rag");
        resolved.Dimensions.Should().Be(16);
    }

    /// <summary>Validator rejects blank connection string at startup.</summary>
    [Fact]
    public void UsePgvector_Validator_RejectsBlankConnectionString()
    {
        var builder = CreateBuilder();
        builder.UsePgvector(opts => opts.Dimensions = 4);

        var provider = builder.Services.BuildServiceProvider();
        var validator = provider.GetServices<IValidateOptions<PgvectorOptions>>()
            .OfType<PgvectorOptionsValidator>()
            .FirstOrDefault();

        validator.Should().NotBeNull();
        var result = validator!.Validate(null, new PgvectorOptions { ConnectionString = "", Dimensions = 4 });
        result.Failed.Should().BeTrue();
    }

    /// <summary>Validator rejects zero or negative dimensions.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void UsePgvector_Validator_RejectsNonPositiveDimensions(int dimensions)
    {
        var builder = CreateBuilder();
        builder.UsePgvector(opts =>
        {
            opts.ConnectionString = "Host=localhost";
            opts.Dimensions = dimensions;
        });

        var provider = builder.Services.BuildServiceProvider();
        var validator = provider.GetServices<IValidateOptions<PgvectorOptions>>()
            .OfType<PgvectorOptionsValidator>()
            .FirstOrDefault();

        validator.Should().NotBeNull();
        var result = validator!.Validate(null, new PgvectorOptions { ConnectionString = "Host=localhost", Dimensions = dimensions });
        result.Failed.Should().BeTrue();
    }

    /// <summary>
    /// UsePgvector(IConfigurationSection) registers the binding, validator, and store.
    /// Ensures the third overload wires up all required services so that renaming
    /// PgvectorOptions.ConnectionString would cause a test failure.
    /// </summary>
    [Fact]
    public void UsePgvector_WithSection_RegistersBindingValidatorAndStore()
    {
        var builder = CreateBuilder();
        var section = Substitute.For<IConfigurationSection>();
        section.Path.Returns("NetIndex:Pgvector");
        section.Key.Returns("Pgvector");

        builder.UsePgvector(section);

        // IConfigureOptions registered by Bind(section)
        builder.Services
            .Should().Contain(sd => sd.ServiceType == typeof(IConfigureOptions<PgvectorOptions>));

        // IVectorStore registered as PgvectorVectorStore via TryAddSingleton
        builder.Services
            .Where(sd => sd.ServiceType == typeof(IVectorStore))
            .Should().HaveCount(1)
            .And.Contain(sd => sd.ImplementationType == typeof(PgvectorVectorStore));

        // Validator registered via TryAddEnumerable
        builder.Services
            .Should().Contain(sd => sd.ServiceType == typeof(IValidateOptions<PgvectorOptions>));
    }
}
