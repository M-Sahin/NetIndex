using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetIndex.Core.Abstractions;
using NetIndex.Storage.Sqlite.Options;

namespace NetIndex.Storage.Sqlite;

/// <summary>Extension methods for configuring SQLite vector storage on <see cref="INetIndexBuilder"/>.</summary>
public static class NetIndexBuilderExtensions
{
    /// <summary>Registers the SQLite vector store with an explicit connection string.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="connectionString">The SQLite connection string (e.g., <c>Data Source=./rag.db</c>).</param>
    /// <param name="configure">Optional delegate for additional <see cref="SqliteOptions"/> configuration.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is null or whitespace.</exception>
    public static INetIndexBuilder UseSqlite(
        this INetIndexBuilder builder,
        string connectionString,
        Action<SqliteOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Register user's configure first so the explicit connectionString argument wins
        if (configure is not null)
        {
            builder.Services.Configure<SqliteOptions>(configure);
        }
        builder.Services.Configure<SqliteOptions>(opts => opts.ConnectionString = connectionString);

        builder.Services.TryAddSingleton<IVectorStore, SqliteVectorStore>();
        return builder;
    }

    /// <summary>Registers the SQLite vector store with an options delegate.</summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="configure">Optional delegate for <see cref="SqliteOptions"/> configuration.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    public static INetIndexBuilder UseSqlite(
        this INetIndexBuilder builder,
        Action<SqliteOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (configure is not null)
        {
            builder.Services.Configure<SqliteOptions>(configure);
        }

        builder.Services.TryAddSingleton<IVectorStore, SqliteVectorStore>();
        return builder;
    }
}
