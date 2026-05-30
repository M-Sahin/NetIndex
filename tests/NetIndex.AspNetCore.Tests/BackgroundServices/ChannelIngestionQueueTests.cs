using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NetIndex.AspNetCore.BackgroundServices;
using NetIndex.AspNetCore.Middleware;
using NetIndex.AspNetCore.Options;
using NetIndex.Core.Abstractions;
using NSubstitute;
using Xunit;

namespace NetIndex.AspNetCore.Tests.BackgroundServices;

/// <summary>Unit tests for <see cref="ChannelIngestionQueue"/>.</summary>
public class ChannelIngestionQueueTests
{
    private static ChannelIngestionQueue CreateQueue(IHttpContextAccessor accessor)
        => new(Microsoft.Extensions.Options.Options.Create(new BackgroundIngestionOptions()), accessor);

    private static async Task<IngestionWorkItem> ReadOneAsync(IIngestionQueue queue)
    {
        await using var enumerator = queue.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        return enumerator.Current;
    }

    /// <summary>A document enqueued is read back as the same document.</summary>
    [Fact]
    public async Task ChannelIngestionQueue_EnqueueThenRead_YieldsSameDocumentAsync()
    {
        var queue = CreateQueue(new HttpContextAccessor());
        var document = Substitute.For<IDocument>();
        document.Id.Returns("doc-1");

        await queue.EnqueueAsync(document);

        var item = await ReadOneAsync(queue);
        item.Document.Id.Should().Be("doc-1");
    }

    /// <summary>Enqueue captures the tenant id and claims from the current HttpContext.</summary>
    [Fact]
    public async Task ChannelIngestionQueue_Enqueue_CapturesTenantFromHttpContextAsync()
    {
        var accessor = new HttpContextAccessor();
        var context = new DefaultHttpContext();
        context.Items[NetIndexTenantMiddleware.TenantContextKey] = "acme";
        context.Items[NetIndexTenantMiddleware.ClaimsContextKey] =
            new Dictionary<string, string> { ["role"] = "admin" };
        accessor.HttpContext = context;
        var queue = CreateQueue(accessor);
        var document = Substitute.For<IDocument>();
        document.Id.Returns("doc-1");

        await queue.EnqueueAsync(document);

        var item = await ReadOneAsync(queue);
        item.TenantId.Should().Be("acme");
        item.Claims.Should().NotBeNull();
        item.Claims!["role"].Should().Be("admin");
    }

    /// <summary>With no current HttpContext the work item carries a null tenant snapshot (no throw).</summary>
    [Fact]
    public async Task ChannelIngestionQueue_Enqueue_NullHttpContext_TenantIsNullAsync()
    {
        var queue = CreateQueue(new HttpContextAccessor());
        var document = Substitute.For<IDocument>();
        document.Id.Returns("doc-1");

        await queue.EnqueueAsync(document);

        var item = await ReadOneAsync(queue);
        item.TenantId.Should().BeNull();
        item.Claims.Should().BeNull();
    }

    /// <summary>Enqueueing a null document throws ArgumentNullException synchronously.</summary>
    [Fact]
    public void ChannelIngestionQueue_Enqueue_NullDocument_Throws()
    {
        var queue = CreateQueue(new HttpContextAccessor());

        var act = () => { _ = queue.EnqueueAsync(null!); };

        act.Should().Throw<ArgumentNullException>();
    }
}
