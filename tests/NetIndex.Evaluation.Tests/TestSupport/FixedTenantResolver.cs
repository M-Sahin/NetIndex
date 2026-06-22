using NetIndex.Core.Abstractions;

namespace NetIndex.Evaluation.Tests.TestSupport;

/// <summary>
/// Allow-lists a single fixed tenant for the evaluation harness, since <c>ITenantResolver</c>
/// is deny-all by default and authorization is checked first on every pipeline operation.
/// </summary>
internal sealed class FixedTenantResolver(string tenantId) : ITenantResolver
{
    public Task<string> ResolveTenantIdAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(tenantId);

    public Task<IReadOnlyDictionary<string, string>> ResolveClaimsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenant"] = tenantId,
        });
}
