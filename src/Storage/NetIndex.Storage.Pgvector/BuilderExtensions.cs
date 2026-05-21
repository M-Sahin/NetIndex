using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Storage.Pgvector.Options;

namespace NetIndex.Storage.Pgvector;

/// <summary>Extension methods for configuring the pgvector vector store on <see cref="INetIndexBuilder"/>.</summary>
public static class NetIndexBuilderExtensions
{
    /// <summary>Registers the pgvector vector store with an explicit connection string.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="configure">Optional delegate for additional <see cref="PgvectorOptions"/> configuration.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is null or whitespace.</exception>
    public static INetIndexBuilder UsePgvector(
        this INetIndexBuilder builder,
        string connectionString,
        Action<PgvectorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var optionsBuilder = builder.Services.AddOptions<PgvectorOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }
        // Explicit positional argument wins over the configure delegate
        optionsBuilder.Configure(opts => opts.ConnectionString = connectionString);
        optionsBuilder.ValidateOnStart();

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<PgvectorOptions>, PgvectorOptionsValidator>());
        builder.Services.TryAddSingleton<IVectorStore, PgvectorVectorStore>();

        return builder;
    }

    /// <summary>Registers the pgvector vector store with an options delegate.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="configure">Optional delegate for <see cref="PgvectorOptions"/> configuration.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    public static INetIndexBuilder UsePgvector(
        this INetIndexBuilder builder,
        Action<PgvectorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var optionsBuilder = builder.Services.AddOptions<PgvectorOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }
        optionsBuilder.ValidateOnStart();

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<PgvectorOptions>, PgvectorOptionsValidator>());
        builder.Services.TryAddSingleton<IVectorStore, PgvectorVectorStore>();

        return builder;
    }

    /// <summary>Registers the pgvector vector store using a configuration section.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="section">The configuration section to bind to <see cref="PgvectorOptions"/>.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> or <paramref name="section"/> is null.</exception>
    public static INetIndexBuilder UsePgvector(
        this INetIndexBuilder builder,
        IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(section);

        builder.Services.AddOptions<PgvectorOptions>()
            .Bind(section)
            .ValidateOnStart();

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<PgvectorOptions>, PgvectorOptionsValidator>());
        builder.Services.TryAddSingleton<IVectorStore, PgvectorVectorStore>();

        return builder;
    }
}
