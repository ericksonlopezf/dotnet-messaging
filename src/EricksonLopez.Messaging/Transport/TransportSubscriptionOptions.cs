// Copyright © Erickson Lopez. MIT License.
namespace EricksonLopez.Messaging.Transport;

/// <summary>
/// Specifies configuration options for a transport subscription.
/// </summary>
public sealed class TransportSubscriptionOptions
{
    /// <summary>
    /// Gets or sets the maximum number of concurrent message executions allowed.
    /// </summary>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>
    /// Gets or sets the number of messages to prefetch from the broker before processing begins.
    /// </summary>
    public int PrefetchCount { get; set; } = 10;

    /// <summary>
    /// Gets or sets the consumer group identifier or queue name used to coordinate competing consumers.
    /// </summary>
    public string? ConsumerGroup { get; set; }
}
