using FluentAssertions;
using Microsoft.SemanticKernel;
using NetIndex.Core.Abstractions;
using NSubstitute;
using Xunit;

namespace NetIndex.SemanticKernel.Tests;

/// <summary>
/// Tests for <see cref="KernelPluginCollectionExtensions.AddNetIndexPlugin"/>.
/// </summary>
public class KernelPluginCollectionExtensionsTests
{
    /// <summary>
    /// Verifies that the plugin is registered under the default name "NetIndex".
    /// </summary>
    [Fact]
    public void AddNetIndexPlugin_DefaultName_RegistersPluginNamedNetIndex()
    {
        var pipeline = Substitute.For<INetIndexPipeline>();
        var plugins = new KernelPluginCollection();

        var plugin = plugins.AddNetIndexPlugin(pipeline);

        plugin.Name.Should().Be("NetIndex");
        plugins.Should().ContainSingle();
        plugins["NetIndex"].Should().BeSameAs(plugin);
    }

    /// <summary>
    /// Verifies that a custom plugin name is honored.
    /// </summary>
    [Fact]
    public void AddNetIndexPlugin_CustomName_RegistersPluginWithCustomName()
    {
        var pipeline = Substitute.For<INetIndexPipeline>();
        var plugins = new KernelPluginCollection();

        var plugin = plugins.AddNetIndexPlugin(pipeline, "MyRag");

        plugin.Name.Should().Be("MyRag");
        plugins["MyRag"].Should().BeSameAs(plugin);
    }

    /// <summary>
    /// Verifies that the created plugin is returned and exposes three functions.
    /// </summary>
    [Fact]
    public void AddNetIndexPlugin_ReturnsTheCreatedPlugin()
    {
        var pipeline = Substitute.For<INetIndexPipeline>();
        var plugins = new KernelPluginCollection();

        var plugin = plugins.AddNetIndexPlugin(pipeline);

        plugin.Should().NotBeNull();
        plugin.FunctionCount.Should().Be(3);
    }

    /// <summary>
    /// Verifies that registering a duplicate plugin name throws and leaves the existing plugin in place.
    /// </summary>
    [Fact]
    public void AddNetIndexPlugin_DuplicateName_ThrowsAndDoesNotReplaceExistingPlugin()
    {
        var pipeline = Substitute.For<INetIndexPipeline>();
        var plugins = new KernelPluginCollection();
        var first = plugins.AddNetIndexPlugin(pipeline);

        var act = () => plugins.AddNetIndexPlugin(pipeline);

        act.Should().Throw<ArgumentException>();
        plugins.Should().ContainSingle();
        plugins["NetIndex"].Should().BeSameAs(first);
    }

    /// <summary>
    /// Verifies that a null plugin collection throws <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public void AddNetIndexPlugin_NullPlugins_ThrowsArgumentNullException()
    {
        ICollection<KernelPlugin>? plugins = null;
        var pipeline = Substitute.For<INetIndexPipeline>();

        var act = () => plugins!.AddNetIndexPlugin(pipeline);

        act.Should().Throw<ArgumentNullException>().WithParameterName("plugins");
    }

    /// <summary>
    /// Verifies that a null pipeline throws <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public void AddNetIndexPlugin_NullPipeline_ThrowsArgumentNullException()
    {
        var plugins = new KernelPluginCollection();

        var act = () => plugins.AddNetIndexPlugin(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("netindexPipeline");
    }

    /// <summary>
    /// Verifies that a null, empty, or whitespace plugin name throws <see cref="ArgumentException"/>.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddNetIndexPlugin_NullOrBlankPluginName_ThrowsArgumentException(string? pluginName)
    {
        var pipeline = Substitute.For<INetIndexPipeline>();
        var plugins = new KernelPluginCollection();

        var act = () => plugins.AddNetIndexPlugin(pipeline, pluginName!);

        act.Should().Throw<ArgumentException>().WithParameterName("pluginName");
    }
}
