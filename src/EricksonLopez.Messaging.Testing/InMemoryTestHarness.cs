// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Transport;

namespace EricksonLopez.Messaging.Testing;

/// <summary>
/// Provides an in-memory transport harness that records published and consumed messages for unit testing assertions.
/// </summary>
/// <remarks>
/// <para>
/// The harness records all messages passed to <see cref="PublishRawAsync"/> in <see cref="PublishedMessages"/>.
/// When <see cref="SubscribeAsync"/> is called, the harness immediately invokes the provided handler delegate
/// and records the outcome (Ack or non-Ack) in <see cref="ConsumedMessages"/>.
/// </para>
/// <para>
/// This design allows straightforward synchronous-style unit tests against the transport layer without
/// requiring a running consumer hosted service.
/// </para>
/// </remarks>
public sealed class InMemoryTestHarness : IMessageTransport
{
    private readonly ConcurrentBag<PublishedMessage> _publishedMessages = new();
    private readonly ConcurrentBag<ConsumedMessage> _consumedMessages = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _waiters = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _consumedWaiters = new();

    /// <summary>
    /// Gets the snapshot of all messages published through this test harness.
    /// </summary>
    public PublishedMessageList PublishedMessages => new(_publishedMessages);

    /// <summary>
    /// Gets the snapshot of all messages delivered to subscription handlers through this test harness.
    /// </summary>
    public ConsumedMessageList ConsumedMessages => new(_consumedMessages);

    /// <inheritdoc />
    public ValueTask<global::EricksonLopez.Result.Result> PublishRawAsync(
        string destination,
        ReadOnlyMemory<byte> payload,
        TransportMessageMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        _publishedMessages.Add(new PublishedMessage(destination, payload, metadata));
        if (_waiters.TryGetValue(metadata.MessageType, out var tcs))
        {
            tcs.TrySetResult(true);
        }

        return ValueTask.FromResult(global::EricksonLopez.Result.Result.Success());
    }

    /// <inheritdoc />
    /// <remarks>
    /// Unlike production transports, the test harness immediately invokes <paramref name="messageHandler"/>
    /// for every message currently in the published bag that targets <paramref name="destination"/>.
    /// This enables synchronous test verification without an event loop.
    /// The Ack/Nack result is recorded in <see cref="ConsumedMessages"/>.
    /// <para>
    /// <strong>Important</strong>: Messages published <em>after</em> this call returns are not automatically
    /// delivered to the handler. To test publish-then-consume flows, publish messages first and then call
    /// <see cref="SubscribeAsync"/>, or use <see cref="WaitUntilConsumedAsync"/> after triggering consumption.
    /// </para>
    /// </remarks>
    public async ValueTask<global::EricksonLopez.Result.Result> SubscribeAsync(
        string destination,
        Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>> messageHandler,
        TransportSubscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(messageHandler);
        ArgumentNullException.ThrowIfNull(options);

        // Deliver all already-published messages for this destination to the handler.
        var pending = _publishedMessages
            .Where(m => m.Destination.Equals(destination, StringComparison.Ordinal))
            .ToArray();

        foreach (var msg in pending)
        {
            var ackResult = await messageHandler(msg.Payload, msg.Metadata, cancellationToken);
            var consumed = new ConsumedMessage(
                Destination: destination,
                Payload: msg.Payload,
                Metadata: msg.Metadata,
                Succeeded: ackResult == TransportAckResult.Ack);
            _consumedMessages.Add(consumed);

            if (_consumedWaiters.TryGetValue(msg.Metadata.MessageType, out var tcs))
            {
                tcs.TrySetResult(true);
            }
        }

        return global::EricksonLopez.Result.Result.Success();
    }

    /// <summary>
    /// Waits asynchronously until at least one message of the specified type has been published or the timeout expires.
    /// </summary>
    /// <param name="messageType">The message type identifier to wait for.</param>
    /// <param name="timeout">The maximum duration to wait before timing out.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains <see langword="true"/>
    /// if the message was published within the timeout; otherwise, <see langword="false"/>.
    /// </returns>
    public async ValueTask<bool> WaitUntilPublishedAsync(
        string messageType,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var tcs = _waiters.GetOrAdd(messageType, _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));

        if (PublishedMessages.Contains(messageType))
        {
            _waiters.TryRemove(messageType, out _);
            return true;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            using (cts.Token.Register(() => tcs.TrySetResult(false)))
            {
                return await tcs.Task;
            }
        }
        finally
        {
            _waiters.TryRemove(messageType, out _);
        }
    }

    /// <summary>
    /// Waits asynchronously until at least one message of the specified type has been consumed by a subscription
    /// handler or the timeout expires.
    /// </summary>
    /// <param name="messageType">The message type identifier to wait for.</param>
    /// <param name="timeout">The maximum duration to wait before timing out.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains <see langword="true"/>
    /// if the message was consumed within the timeout; otherwise, <see langword="false"/>.
    /// </returns>
    public async ValueTask<bool> WaitUntilConsumedAsync(
        string messageType,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var tcs = _consumedWaiters.GetOrAdd(messageType, _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));

        if (ConsumedMessages.Contains(messageType))
        {
            _consumedWaiters.TryRemove(messageType, out _);
            return true;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            using (cts.Token.Register(() => tcs.TrySetResult(false)))
            {
                return await tcs.Task;
            }
        }
        finally
        {
            _consumedWaiters.TryRemove(messageType, out _);
        }
    }

    /// <summary>
    /// Asynchronously releases the resources used by this instance.
    /// </summary>
    /// <returns>A value task representing the asynchronous disposal operation.</returns>
    public ValueTask DisposeAsync() => default;
}

