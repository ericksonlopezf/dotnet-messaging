// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.Contracts;

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
