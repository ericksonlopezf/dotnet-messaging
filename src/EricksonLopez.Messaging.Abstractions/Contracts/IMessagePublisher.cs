// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Contracts;

using EricksonLopez.Result;

/// <summary>
/// Defines a message publisher for emitting strongly-typed messages to distributed brokers.
/// </summary>
public interface IMessagePublisher
{
    /// <summary>
    /// Publishes a message to all interested subscribers.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message payload.</typeparam>
    /// <param name="message">The message instance to publish.</param>
    /// <param name="options">The publish configuration tailoring destination, headers, and keys, if specified.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains a <see cref="Result"/>
    /// indicating whether the transport acknowledged the message.
    /// </returns>
    ValueTask<Result> PublishAsync<TMessage>(
        TMessage message,
        MessagePublishOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : notnull;

    /// <summary>
    /// Sends a point-to-point command message directly to a target destination or queue.
    /// </summary>
    /// <typeparam name="TMessage">The type of the command message payload.</typeparam>
    /// <param name="message">The message instance to send.</param>
    /// <param name="destination">The target queue, topic, or endpoint name.</param>
    /// <param name="options">The send configuration tailoring headers and metadata, if specified.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains a <see cref="Result"/>
    /// indicating whether the transport acknowledged the message.
    /// </returns>
    ValueTask<Result> SendAsync<TMessage>(
        TMessage message,
        string destination,
        MessageSendOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : notnull;

    /// <summary>
    /// Publishes a batch of messages to all interested subscribers in a single operation.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message payload.</typeparam>
    /// <param name="messages">The collection of message instances to publish.</param>
    /// <param name="options">The publish configuration tailoring destination, headers, and keys, if specified.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains a <see cref="Result"/>
    /// indicating whether the batch was acknowledged.
    /// </returns>
    ValueTask<Result> PublishBatchAsync<TMessage>(
        IEnumerable<TMessage> messages,
        MessagePublishOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : notnull;

    /// <summary>
    /// Sends a batch of point-to-point command messages directly to a target destination or queue in a single operation.
    /// </summary>
    /// <typeparam name="TMessage">The type of the command message payload.</typeparam>
    /// <param name="messages">The collection of message instances to send.</param>
    /// <param name="destination">The target queue, topic, or endpoint name.</param>
    /// <param name="options">The send configuration tailoring headers and metadata, if specified.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains a <see cref="Result"/>
    /// indicating whether the batch was acknowledged.
    /// </returns>
    ValueTask<Result> SendBatchAsync<TMessage>(
        IEnumerable<TMessage> messages,
        string destination,
        MessageSendOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : notnull;
}
