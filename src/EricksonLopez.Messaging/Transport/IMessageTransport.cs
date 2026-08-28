// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Transport;

using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;

/// <summary>
/// Defines a physical transport driver for publishing and subscribing to messaging destinations.
/// </summary>
public interface IMessageTransport : IAsyncDisposable
{
    /// <summary>
    /// Publishes a raw serialized envelope to the specified broker destination.
    /// </summary>
    /// <param name="destination">The target topic, queue, or exchange.</param>
    /// <param name="payload">The raw serialized payload bytes.</param>
    /// <param name="metadata">The associated metadata headers.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains a <see cref="Result"/>
    /// indicating the transport acknowledgement outcome.
    /// </returns>
    ValueTask<Result> PublishRawAsync(
        string destination,
        ReadOnlyMemory<byte> payload,
        TransportMessageMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts consuming messages from the specified destination using the provided callback.
    /// </summary>
    /// <param name="destination">The destination queue or topic name.</param>
    /// <param name="messageHandler">The callback invoked for each received message packet.</param>
    /// <param name="options">The subscription concurrency and prefetch options.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains a <see cref="Result"/>
    /// indicating the subscription outcome.
    /// </returns>
    ValueTask<Result> SubscribeAsync(
        string destination,
        Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>> messageHandler,
        TransportSubscriptionOptions options,
        CancellationToken cancellationToken = default);
}




