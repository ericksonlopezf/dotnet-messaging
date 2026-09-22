// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
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
    /// <exception cref="ArgumentException"><paramref name="messageType"/> is <see langword="null"/>, empty, or consists only of white-space characters.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="metadata"/> or <paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> has been cancelled before or during dispatch.</exception>
    ValueTask<Result> DispatchAsync(
        string messageType,
        ReadOnlyMemory<byte> payload,
        TransportMessageMetadata metadata,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deserializes a batch of message items and executes their corresponding handlers through the middleware pipeline.
    /// </summary>
    /// <param name="items">The collection of message items to dispatch.</param>
    /// <param name="serviceProvider">The service provider for dependency resolution within the execution scope.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains a <see cref="Result"/>
    /// indicating whether all items in the batch dispatched successfully or if any item failed.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> or <paramref name="serviceProvider"/> is <see langword="null"/></exception>
    ValueTask<Result> DispatchBatchAsync(
        IEnumerable<MessageDispatchItem> items,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        return DispatchBatchInternalAsync(items, serviceProvider, cancellationToken);

        async ValueTask<Result> DispatchBatchInternalAsync(
            IEnumerable<MessageDispatchItem> batchItems,
            IServiceProvider sp,
            CancellationToken ct)
        {
            foreach (var item in batchItems)
            {
                ct.ThrowIfCancellationRequested();
                var result = await DispatchAsync(item.MessageType, item.Payload, item.Metadata, sp, ct).ConfigureAwait(false);
                if (result.IsFailure)
                {
                    return result;
                }
            }
            return Result.Success();
        }
    }
}




