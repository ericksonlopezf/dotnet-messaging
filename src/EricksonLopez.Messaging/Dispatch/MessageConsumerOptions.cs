// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Messaging.Transport;

namespace EricksonLopez.Messaging.Dispatch;

/// <summary>
/// Provides configuration options for <see cref="MessageConsumer"/> execution.
/// </summary>
public sealed class MessageConsumerOptions
{
    /// <summary>
    /// Gets or sets the acknowledgement outcome to return when message dispatch fails functionally and no
    /// <see cref="EricksonLopez.Messaging.Contracts.IDeadLetterQueue"/> is registered.
    /// The default is <see cref="TransportAckResult.Ack"/> to prevent poison-message head-of-line blocking in unconfigured local queues.
    /// When using physical brokers with native dead-letter queues, set this to <see cref="TransportAckResult.DeadLetter"/>.
    /// </summary>
    public TransportAckResult UnhandledFailureAckResult { get; set; } = TransportAckResult.Ack;

    /// <summary>
    /// Gets or sets the maximum number of concurrent message handlers executing simultaneously.
    /// Default is <c>Environment.ProcessorCount * 2</c> (minimum 1).
    /// </summary>
    public int MaxConcurrency { get; set; } = Math.Max(1, Environment.ProcessorCount * 2);

    /// <summary>
    /// Gets or sets the number of messages to prefetch from the underlying transport.
    /// Default is 20.
    /// </summary>
    public int PrefetchCount { get; set; } = 20;
}
