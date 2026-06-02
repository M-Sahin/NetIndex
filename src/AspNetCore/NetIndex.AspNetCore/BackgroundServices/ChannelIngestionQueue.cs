using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NetIndex.AspNetCore.Middleware;
using NetIndex.AspNetCore.Options;
using NetIndex.Core.Abstractions;

namespace NetIndex.AspNetCore.BackgroundServices;

/// <summary>
/// A bounded <see cref="Channel{T}"/>-backed <see cref="IIngestionQueue"/>. Captures the tenant
/// context from the current request's <see cref="HttpContext"/> at enqueue time so the background
/// consumer can replay it for authorization.
/// </summary>
internal sealed class ChannelIngestionQueue : IIngestionQueue
{
    private readonly Channel<IngestionWorkItem> _channel;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ChannelIngestionQueue(
        IOptions<BackgroundIngestionOptions> options,
        IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpContextAccessor);

        var value = options.Value;
        _channel = Channel.CreateBounded<IngestionWorkItem>(new BoundedChannelOptions(value.QueueCapacity)
        {
            FullMode = value.FullMode,
            SingleReader = true,
            SingleWriter = false,
        });
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public ValueTask EnqueueAsync(IDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Snapshot the tenant context now, while the request's HttpContext still exists. The
        // background worker has no HttpContext of its own, so deferring this read would lose it.
        var items = _httpContextAccessor.HttpContext?.Items;
        var tenantId = items?[NetIndexTenantMiddleware.TenantContextKey] as string;

        // Defensive copy of the claims dictionary — capture an immutable snapshot at enqueue time
        // so the background consumer holds its own copy, not a reference into the live request
        // Items dictionary that will be cleared when the request ends (AC-2).
        IReadOnlyDictionary<string, string>? claims = null;
        if (items?[NetIndexTenantMiddleware.ClaimsContextKey] is IDictionary<string, string> liveClaims)
        {
            // Preserve OrdinalIgnoreCase so the background consumer's claim lookups behave
            // identically to the request path (AC-2: defensive copy must not drop the comparer).
            claims = new Dictionary<string, string>(liveClaims, StringComparer.OrdinalIgnoreCase);
        }

        var workItem = new IngestionWorkItem(document, tenantId, claims);
        return _channel.Writer.WriteAsync(workItem, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<IngestionWorkItem> ReadAllAsync(CancellationToken cancellationToken = default)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
