namespace NetIndex.Core.Options;

/// <summary>
/// Controls tenant-isolation behaviour in the retrieval pipeline.
/// </summary>
public sealed class TenantFilteringOptions
{
    /// <summary>
    /// The default number of results to request from the vector store before tenant filtering.
    /// </summary>
    public const int DefaultQueryTop = 5;

    /// <summary>
    /// Maximum number of chunks fetched from the store in a single query (hard ceiling).
    /// </summary>
    public const int MaxFetchCount = 500;

    /// <summary>
    /// How many times the requested <c>top</c> to over-fetch from the store before filtering
    /// by tenant so that higher-scoring chunks from other tenants do not crowd out the caller's
    /// own results. Default: 5.
    /// </summary>
    /// <remarks>
    /// V1 mitigation: in extreme skew scenarios where one tenant dominates the global vector
    /// space, results may still be incomplete. A store-level tenant predicate is the long-term
    /// remedy; this factor is a configurable tuning knob until then.
    /// </remarks>
    public int OverFetchFactor { get; set; } = 5;
}
