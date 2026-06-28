using NetIndex.Core.Abstractions;

namespace NetIndex.Samples.LocalSmoke;

// DEV ONLY: allows all operations, satisfying the deny-all default. Never use in production.
internal sealed class LocalDevTenantResolver : ITenantResolver
{
    private const string DevTenantId = "local-dev";

    private static readonly IReadOnlyDictionary<string, string> Claims =
        new Dictionary<string, string> { ["tenant_id"] = DevTenantId };

    public Task<string> ResolveTenantIdAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DevTenantId);
    }

    public Task<IReadOnlyDictionary<string, string>> ResolveClaimsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Claims);
    }
}

internal sealed record SampleDocument(string Id, string Content) : IDocument
{
    public IReadOnlyDictionary<string, string>? Metadata => null;
    public Uri? SourceUri => null;
}
