// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Contracts;

/// <summary>
/// Defines an abstraction for verifying message idempotency and acquiring processing rights to prevent duplicate execution.
/// </summary>
public interface IMessageDeduplicationStore : IDisposable
{
    /// <summary>
    /// Attempts to acquire an idempotency lock for the specified message identity.
    /// </summary>
    /// <param name="messageId">The unique identity of the message.</param>
    /// <param name="expiration">The maximum duration to retain the deduplication state.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains <see langword="true"/>
    /// if the lock was successfully acquired; otherwise, <see langword="false"/> if the message is a duplicate.
    /// </returns>
    ValueTask<bool> TryAcquireAsync(string messageId, TimeSpan expiration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the idempotency lock for the specified message identity, allowing it to be retried.
    /// </summary>
    /// <param name="messageId">The unique identity of the message.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A value task representing the asynchronous operation.</returns>
    ValueTask ReleaseAsync(string messageId, CancellationToken cancellationToken = default);
}
