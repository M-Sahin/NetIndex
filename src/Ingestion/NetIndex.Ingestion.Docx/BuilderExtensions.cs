using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Docx.Loaders;
using NetIndex.Ingestion.Docx.Options;

namespace NetIndex.Ingestion.Docx;

/// <summary>
/// Extension methods for registering DOCX ingestion services on <see cref="INetIndexBuilder"/>.
/// </summary>
public static class NetIndexBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="WordDocumentLoader"/> as the <see cref="IDocumentLoader{DocxFormat}"/> implementation.
    /// </summary>
    /// <param name="builder">The <see cref="INetIndexBuilder"/> to configure.</param>
    /// <param name="configure">Optional delegate to configure <see cref="WordDocumentLoaderOptions"/>.</param>
    /// <returns>The same <see cref="INetIndexBuilder"/> for fluent chaining.</returns>
    public static INetIndexBuilder UseWordDocumentLoader(
        this INetIndexBuilder builder,
        Action<WordDocumentLoaderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (configure is not null)
        {
            builder.Services.Configure(configure);
        }
        builder.Services.TryAddSingleton<IDocumentLoader<DocxFormat>, WordDocumentLoader>();
        return builder;
    }
}
