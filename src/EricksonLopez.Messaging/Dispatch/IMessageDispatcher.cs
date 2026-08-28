// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Dispatch;

using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;

/// <summary>
/// Defines a message dispatcher for routing incoming raw payloads and metadata to registered handlers.
/// </summary>
public interface IMessageDispatcher
{
    /// <summary>
    /// Deserializes the message payload and executes the corresponding handler through the middleware pipeline.
    /// </summary>
    /// <param name="messageType">The message type identifier.</param>
    /// <param name="payload">The raw message payload bytes.</param>
    /// <param name="metadata">The accompanying message metadata.</param>
    /// <param name="serviceProvider">The service provider for dependency resolution within the execution scope.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains a <see cref="Result"/>
    /// indicating the dispatch and handler execution outcome.
    /// </returns>
    ValueTask<Result> DispatchAsync(
        string messageType,
        ReadOnlyMemory<byte> payload,
        TransportMessageMetadata metadata,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default);
}




