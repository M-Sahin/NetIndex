using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Markdown.Loaders;

namespace NetIndex.Ingestion.Markdown;

/// <summary>
/// Extension methods for registering Markdown ingestion services on <see cref="INetIndexBuilder"/>.
/// </summary>
public static class NetIndexBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="MarkdownDocumentLoader"/> as the <see cref="IDocumentLoader{MarkdownFormat}"/> implementation.
    /// </summary>
    /// <param name="builder">The <see cref="INetIndexBuilder"/> to configure.</param>
    /// <returns>The same <see cref="INetIndexBuilder"/> for fluent chaining.</returns>
    public static INetIndexBuilder UseMarkdownDocumentLoader(this INetIndexBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<IDocumentLoader<MarkdownFormat>, MarkdownDocumentLoader>();
        return builder;
    }
}
