using System.Threading.Channels;

namespace NetIndex.AspNetCore.Options;

/// <summary>
/// Options for the background ingestion queue and hosted service.
/// </summary>
public sealed class BackgroundIngestionOptions
{
    /// <summary>
    /// Gets or sets the maximum number of pending work items the bounded queue holds before
    /// <see cref="FullMode"/> takes effect. Must be greater than zero. Defaults to <c>100</c>.
    /// </summary>
    public int QueueCapacity { get; set; } = 100;

    /// <summary>
    /// Gets or sets the behaviour applied when the queue is full. The default,
    /// <see cref="BoundedChannelFullMode.Wait"/>, applies backpressure so producers await capacity
    /// rather than dropping documents.
    /// </summary>
    public BoundedChannelFullMode FullMode { get; set; } = BoundedChannelFullMode.Wait;
}
