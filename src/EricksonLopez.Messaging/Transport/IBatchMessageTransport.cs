// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Transport;

using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;

/// <summary>
/// Defines a message transport specialization supporting batch publishing of raw serialized messages.
/// </summary>
public interface IBatchMessageTransport : IMessageTransport
{
    /// <summary>
    /// Publishes a batch of raw serialized envelopes to the specified broker destination in a single operation.
    /// </summary>
    /// <param name="destination">The target topic, queue, or exchange.</param>
    /// <param name="batch">The collection of payload and metadata items to publish.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains a <see cref="Result"/>
    /// indicating the batch publication outcome.
    /// </returns>
    ValueTask<Result> PublishBatchRawAsync(
        string destination,
        IReadOnlyList<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)> batch,
        CancellationToken cancellationToken = default);
}

