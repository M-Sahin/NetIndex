using Microsoft.Extensions.DependencyInjection;
using NetIndex.Core.Abstractions;
using NetIndex.Storage.InMemory;
using NetIndex.Storage.InMemory.Options;
using NSubstitute;

namespace NetIndex.Storage.InMemory.Tests;

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

    /// <summary>UseInMemoryVectorStore with null builder throws ArgumentNullException.</summary>
    [Fact]
    public void UseInMemoryVectorStore_WithNullBuilder_ThrowsArgumentNullException()
    {
        INetIndexBuilder? builder = null;
        Assert.Throws<ArgumentNullException>(() => builder!.UseInMemoryVectorStore());
    }

    /// <summary>UseInMemoryVectorStore with no args registers IVectorStore as InMemoryVectorStore.</summary>
    [Fact]
    public void UseInMemoryVectorStore_WithNoArgs_RegistersIVectorStore()
    {
        var builder = CreateBuilder();
        builder.UseInMemoryVectorStore();
        var provider = builder.Services.BuildServiceProvider();

        var store = provider.GetRequiredService<IVectorStore>();

        Assert.NotNull(store);
        Assert.IsType<InMemoryVectorStore>(store);
    }

    /// <summary>UseInMemoryVectorStore with configure delegate applies the dimensions setting.</summary>
    [Fact]
    public void UseInMemoryVectorStore_WithConfigureDelegate_SetsOptions()
    {
        var builder = CreateBuilder();
        builder.UseInMemoryVectorStore(opts => opts.Dimensions = 768);
        var provider = builder.Services.BuildServiceProvider();

        var store = provider.GetRequiredService<IVectorStore>();

        Assert.Equal(768, store.Dimensions);
    }

    /// <summary>Calling UseInMemoryVectorStore twice registers only one IVectorStore (TryAddSingleton).</summary>
    [Fact]
    public void UseInMemoryVectorStore_CalledTwice_DoesNotRegisterDuplicate()
    {
        var builder = CreateBuilder();
        builder.UseInMemoryVectorStore();
        builder.UseInMemoryVectorStore();

        var registrations = builder.Services.Count(d => d.ServiceType == typeof(IVectorStore));

        Assert.Equal(1, registrations);
    }
}
