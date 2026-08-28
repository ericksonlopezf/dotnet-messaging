// Copyright © Erickson Lopez. MIT License.
using EricksonLopez.Result;

namespace EricksonLopez.Messaging.Transport;

/// <summary>
/// Specifies acknowledgment outcomes returned to the transport after processing a message.
/// </summary>
public enum TransportAckResult
{
    /// <summary>
    /// Indicates that message processing succeeded and the message should be acknowledged and removed from the queue.
    /// </summary>
    Ack = 0,

    /// <summary>
    /// Indicates that a transient failure occurred and the message should be rejected and requeued for retry.
    /// </summary>
    NackRequeue = 1,

    /// <summary>
    /// Indicates that a fatal failure occurred and the message should be routed to a dead-letter queue.
    /// </summary>
    DeadLetter = 2
}

