using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Ingestion.Tesseract.Internal;
using NetIndex.Ingestion.Tesseract.Options;

namespace NetIndex.Ingestion.Tesseract;

/// <summary>
/// Extension methods for registering Tesseract OCR services on <see cref="INetIndexBuilder"/>.
/// </summary>
public static class NetIndexBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="TesseractVisionExtractor"/> as the <see cref="IVisionExtractor"/> implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Calling <c>UseTesseract</c> more than once is safe: the second and subsequent calls register
    /// no additional singleton or validator instances. Options <see cref="Action{TesseractOptions}"/>
    /// delegates accumulate in registration order, with the last value written for each property winning.
    /// </para>
    /// <para>
    /// Option validation (including <c>TessDataPath</c> existence and tessdata file presence) runs during
    /// <c>INetIndexBuilder.Build()</c>. No native Tesseract library is loaded at that point.
    /// </para>
    /// </remarks>
    /// <param name="builder">The <see cref="INetIndexBuilder"/> to configure.</param>
    /// <param name="configure">Optional delegate to configure <see cref="TesseractOptions"/>.</param>
    /// <returns>The same <see cref="INetIndexBuilder"/> for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    public static INetIndexBuilder UseTesseract(
        this INetIndexBuilder builder,
        Action<TesseractOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var optionsBuilder = builder.Services.AddOptions<TesseractOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TesseractOptions>, TesseractOptionsValidator>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<INetIndexBuildValidator, TesseractBuildValidator>());
        builder.Services.TryAddSingleton<IVisionExtractor, TesseractVisionExtractor>();

        return builder;
    }
}
