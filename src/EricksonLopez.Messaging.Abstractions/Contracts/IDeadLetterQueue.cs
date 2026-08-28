// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
namespace EricksonLopez.Messaging.Contracts;

using EricksonLopez.Result;

/// <summary>
/// Represents a structured reason descriptor detailing why a message was diverted to a dead-letter queue.
/// </summary>
/// <param name="ReasonCode">The standardized classification code (e.g., <c>DESERIALIZATION_FAILURE</c>, <c>MAX_RETRIES_EXCEEDED</c>, <c>POISON_PAYLOAD</c>).</param>
/// <param name="Description">The human-readable explanation of the processing or transport failure.</param>
/// <param name="ExceptionType">The fully qualified type name of the originating exception, if available.</param>
/// <param name="StackTrace">The stack trace capture of the failure site, if available.</param>
/// <param name="OccurredAtUtc">The timestamp when the dead-letter routing was triggered.</param>
public sealed record DeadLetterReason(
    string ReasonCode,
    string Description,
    string? ExceptionType = null,
    string? StackTrace = null,
    DateTimeOffset OccurredAtUtc = default)
{
    /// <summary>
    /// Creates a new <see cref="DeadLetterReason"/> instance from an optional originating exception.
    /// </summary>
    /// <param name="reasonCode">The standardized failure classification code.</param>
    /// <param name="description">The descriptive explanation of the failure.</param>
    /// <param name="exception">The captured exception causing the dead-letter redirect, if available.</param>
    /// <returns>A new <see cref="DeadLetterReason"/> instance containing exception diagnostics.</returns>
    public static DeadLetterReason FromException(string reasonCode, string description, Exception? exception = null)
    {
        return new DeadLetterReason(
            ReasonCode: reasonCode,
            Description: description,
            ExceptionType: exception?.GetType().FullName,
            StackTrace: exception?.StackTrace,
            OccurredAtUtc: DateTimeOffset.UtcNow);
    }
}

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
