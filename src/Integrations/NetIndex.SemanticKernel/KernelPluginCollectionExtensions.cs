using System;
using System.Collections.Generic;
using Microsoft.SemanticKernel;
using NetIndex.Core.Abstractions;
using NetIndex.SemanticKernel.Internal;

namespace NetIndex.SemanticKernel;

/// <summary>
/// Extension methods for registering NetIndex as a Semantic Kernel plugin.
/// </summary>
public static class KernelPluginCollectionExtensions
{
    /// <summary>
    /// Creates a <see cref="KernelPlugin"/> that exposes NetIndex retrieval, ingestion, and
    /// generation as Semantic Kernel functions, and adds it to <paramref name="plugins"/>.
    /// </summary>
    /// <param name="plugins">The plugin collection to add the NetIndex plugin to, typically <c>kernel.Plugins</c>.</param>
    /// <param name="netindexPipeline">The configured NetIndex pipeline backing the plugin's functions.</param>
    /// <param name="pluginName">The name to register the plugin under. Defaults to <c>"NetIndex"</c>.</param>
    /// <returns>The <see cref="KernelPlugin"/> that was added to <paramref name="plugins"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="plugins"/> or <paramref name="netindexPipeline"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="pluginName"/> is <c>null</c>, empty, or whitespace.</exception>
    /// <remarks>
    /// All three functions (<c>RetrieveChunks</c>, <c>IngestDocument</c>, <c>GenerateAnswer</c>) delegate to
    /// <paramref name="netindexPipeline"/>, so authorization, tenant isolation, and provider behavior remain
    /// owned by NetIndex. Duplicate plugin names follow Semantic Kernel's own collection contract; this method
    /// does not silently replace an existing plugin.
    /// </remarks>
    public static KernelPlugin AddNetIndexPlugin(
        this ICollection<KernelPlugin> plugins,
        INetIndexPipeline netindexPipeline,
        string pluginName = "NetIndex")
    {
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(netindexPipeline);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);

        var plugin = KernelPluginFactory.CreateFromObject(new NetIndexPlugin(netindexPipeline), pluginName);
        plugins.Add(plugin);
        return plugin;
    }
}
