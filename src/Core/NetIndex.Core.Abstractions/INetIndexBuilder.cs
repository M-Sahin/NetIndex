using System.Threading;

namespace NetIndex.Core.Abstractions;

/// <summary>
/// Fluent builder for configuring the NetIndex RAG pipeline.
/// </summary>
/// <remarks>
/// Canonical noun #1 in NOUNS.md.
/// 
/// This interface is the extension point for all <c>Use{Feature}(...)</c> methods.
/// Feature packages (providers, storage, ingestion) register their configuration
/// via extension methods on this interface — not via direct service collection calls.
/// 
/// Example:
/// <code>
/// var builder = services.AddNetIndex();
///     builder.UseOllamaEmbedding();
///     builder.UseSqliteVectorStore(connectionString);
///     var pipeline = builder.Build();
/// </code>
/// </remarks>
public interface INetIndexBuilder
{
    /// <summary>
    /// Finalizes configuration and returns the composed pipeline orchestrator.
    /// </summary>
    /// <returns>The configured pipeline instance ready for ingestion and query operations.</returns>
    /// <remarks>
    /// The actual pipeline type (<c>NetIndexPipeline</c>) is implemented in the
    /// <c>NetIndex.Core</c> package (Epic 2, Story 2.4). This interface only defines the
    /// contract; feature packages may call <c>Build()</c> to obtain the orchestrator at
    /// runtime.
    /// </remarks>
    object Build();
}
