// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Transport;

using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;

/// <summary>
/// Defines a message transport specialization supporting scheduled message delivery with a configured delay.
/// </summary>
public interface IDeferableMessageTransport : IMessageTransport
{
    /// <summary>
    /// Defers the publishing of a raw serialized envelope to the specified broker destination with a configured delay.
    /// </summary>
    /// <param name="destination">The target topic, queue, or exchange.</param>
    /// <param name="payload">The raw serialized payload bytes.</param>
    /// <param name="metadata">The associated metadata headers.</param>
    /// <param name="delay">The duration to delay delivery before making the message available to consumers.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains a <see cref="Result"/>
    /// indicating the transport acknowledgement outcome.
    /// </returns>
    ValueTask<Result> DeferRawAsync(
        string destination,
        ReadOnlyMemory<byte> payload,
        TransportMessageMetadata metadata,
        TimeSpan delay,
        CancellationToken cancellationToken = default);
}

