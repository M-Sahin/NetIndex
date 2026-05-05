using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Pdf.Loaders;
using NetIndex.Ingestion.Pdf.Options;

namespace NetIndex.Ingestion.Pdf;

/// <summary>
/// Extension methods for registering PDF ingestion services on <see cref="INetIndexBuilder"/>.
/// </summary>
public static class NetIndexBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="PdfDocumentLoader"/> as the <see cref="IDocumentLoader{PdfFormat}"/> implementation.
    /// </summary>
    /// <param name="builder">The <see cref="INetIndexBuilder"/> to configure.</param>
    /// <param name="configure">Optional delegate to configure <see cref="PdfDocumentLoaderOptions"/>.</param>
    /// <returns>The same <see cref="INetIndexBuilder"/> for fluent chaining.</returns>
    public static INetIndexBuilder UsePdfDocumentLoader(
        this INetIndexBuilder builder,
        Action<PdfDocumentLoaderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (configure is not null)
        {
            builder.Services.Configure<PdfDocumentLoaderOptions>(configure);
        }
        builder.Services.TryAddSingleton<IDocumentLoader<PdfFormat>, PdfDocumentLoader>();
        return builder;
    }
}
