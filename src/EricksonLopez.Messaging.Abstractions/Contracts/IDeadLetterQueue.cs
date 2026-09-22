// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Contracts;

using EricksonLopez.Result;

/// <summary>
/// Defines a mechanism for routing poisoned, invalid, or permanently failing messages to a durable dead-letter queue.
/// </summary>
public interface IDeadLetterQueue
{
    /// <summary>
    /// Forwards a strongly-typed message instance to the dead-letter destination along with diagnostic reason metadata.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message payload.</typeparam>
    /// <param name="message">The message instance that failed processing.</param>
    /// <param name="reason">The structured reason justifying dead-letter routing.</param>
    /// <param name="context">The ambient message consumption context, if present.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains a <see cref="Result"/>
    /// indicating whether the dead-letter routing succeeded.
    /// </returns>
    ValueTask<Result> ForwardToDeadLetterAsync<TMessage>(
        TMessage message,
        DeadLetterReason reason,
        MessageContext? context = null,
        CancellationToken cancellationToken = default) where TMessage : notnull;

    /// <summary>
    /// Forwards an unparseable or raw binary payload to the dead-letter destination along with transport metadata.
    /// </summary>
    /// <param name="rawPayload">The raw undecodable byte payload.</param>
    /// <param name="reason">The structured reason justifying dead-letter routing.</param>
    /// <param name="metadata">The transport metadata associated with the failed frame, if extractable.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains a <see cref="Result"/>
    /// indicating whether the dead-letter routing succeeded.
    /// </returns>
    ValueTask<Result> ForwardRawToDeadLetterAsync(
        ReadOnlyMemory<byte> rawPayload,
        DeadLetterReason reason,
        TransportMessageMetadata? metadata = null,
        CancellationToken cancellationToken = default);
}
