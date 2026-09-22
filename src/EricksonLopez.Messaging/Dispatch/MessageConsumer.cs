// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Dispatch;

using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Diagnostics;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

/// <summary>
/// Provides message consumption management including endpoint subscriptions, concurrency limits, scoped execution, and graceful shutdown.
/// </summary>
public sealed class MessageConsumer : IMessageConsumer, IAsyncDisposable, IDisposable
{
    private readonly IMessageTransport _transport;
    private readonly IMessageDispatcher _dispatcher;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MessageConsumer> _logger;
    private readonly MessageConsumerOptions _options;
    private readonly List<string> _subscribedDestinations;
    private readonly CancellationTokenSource _cts = new();
    private int _inFlightCount;
    private TaskCompletionSource? _drainTcs;
    private readonly object _drainLock = new();
    private volatile bool _isAcceptingMessages = true;
    private int _started;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageConsumer"/> class with the specified transport, dispatcher, and scope factory.
    /// </summary>
    /// <param name="transport">The message transport implementation.</param>
    /// <param name="dispatcher">The message dispatcher for processing received payloads.</param>
    /// <param name="scopeFactory">The service scope factory for creating message execution scopes.</param>
    /// <param name="subscribedDestinations">The collection of destination queues or topics to subscribe to, if specified.</param>
    /// <param name="registrations">The collection of discovered handler registrations, if specified.</param>
    /// <param name="logger">The logger instance, if specified.</param>
    /// <param name="options">The consumer options, if specified.</param>
    /// <exception cref="ArgumentNullException"><paramref name="transport"/>, <paramref name="dispatcher"/>, or <paramref name="scopeFactory"/> is <see langword="null"/></exception>
    public MessageConsumer(
        IMessageTransport transport,
        IMessageDispatcher dispatcher,
        IServiceScopeFactory scopeFactory,
        IEnumerable<string>? subscribedDestinations = null,
        IEnumerable<IHandlerRegistration>? registrations = null,
        ILogger<MessageConsumer>? logger = null,
        IOptions<MessageConsumerOptions>? options = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? NullLogger<MessageConsumer>.Instance;
        _options = options?.Value ?? new MessageConsumerOptions();
        _subscribedDestinations = subscribedDestinations != null ? new List<string>(subscribedDestinations) : new List<string>();

        if (registrations != null)
        {
            foreach (var reg in registrations)
            {
                if (reg is HandlerRegistrationBase baseReg && !_subscribedDestinations.Contains(baseReg.TypeName))
                {
                    _subscribedDestinations.Add(baseReg.TypeName);
                }
            }
        }
    }

    /// <summary>
    /// Adds an additional destination queue or topic for subscription prior to starting the consumer.
    /// </summary>
    /// <param name="destination">The destination queue or topic name to subscribe to.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public void AddDestination(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        lock (_subscribedDestinations)
        {
            if (!_subscribedDestinations.Contains(destination))
            {
                _subscribedDestinations.Add(destination);
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Subscribes to all registered destinations sequentially. Each destination is configured with
    /// a concurrency limit of <c>Environment.ProcessorCount * 2</c> and a prefetch count of <c>20</c>
    /// by default. These values are currently not configurable externally; use a custom
    /// <see cref="IMessageConsumer"/> implementation if different concurrency settings are required.
    /// </remarks>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;

        string[] destinations;
        lock (_subscribedDestinations)
        {
            destinations = _subscribedDestinations.ToArray();
        }

        foreach (var destination in destinations)
        {
            var subOptions = new TransportSubscriptionOptions
            {
                MaxConcurrency = _options.MaxConcurrency,
                PrefetchCount = _options.PrefetchCount
            };

            await _transport.SubscribeAsync(
                destination,
                OnRawMessageReceivedAsync,
                subOptions,
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Consumer subscribed to destination: {Destination}", destination);
        }
    }

    /// <inheritdoc />
    public ValueTask StopReceivingAsync()
    {
        _isAcceptingMessages = false;
        _logger.LogInformation("Consumer stopped accepting new messages.");
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DrainInFlightMessagesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Draining in-flight messages...");
        Task drainTask;
        lock (_drainLock)
        {
            if (Volatile.Read(ref _inFlightCount) == 0)
            {
                _logger.LogInformation("In-flight messages successfully drained.");
                return;
            }

            if (_drainTcs is null || _drainTcs.Task.IsCompleted)
            {
                _drainTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            drainTask = _drainTcs.Task;
        }

        await drainTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("In-flight messages successfully drained.");
    }

    private async ValueTask<TransportAckResult> OnRawMessageReceivedAsync(
        ReadOnlyMemory<byte> payload,
        TransportMessageMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (!_isAcceptingMessages)
        {
            // Reject and re-queue because host is shutting down
            return TransportAckResult.NackRequeue;
        }

        MessagingDiagnostics.MessagesReceived.Add(1);
        Interlocked.Increment(ref _inFlightCount);
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            var effectiveCt = linkedCts.Token;

            using var scope = _scopeFactory.CreateScope();
            var result = await _dispatcher.DispatchAsync(
                metadata.MessageType,
                payload,
                metadata,
                scope.ServiceProvider,
                effectiveCt).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                return TransportAckResult.Ack;
            }

            if (result.Error.Code == "Messaging.Cancelled" || cancellationToken.IsCancellationRequested || _cts.IsCancellationRequested)
            {
                _logger.LogInformation("Message processing cancelled during shutdown for {MessageType}", metadata.MessageType);
                return TransportAckResult.NackRequeue;
            }

            _logger.LogWarning("Message processing returned failure: {Error}", result.Error.Description);

            var dlq = scope.ServiceProvider.GetService<IDeadLetterQueue>();
            if (dlq is not null)
            {
                var reason = new DeadLetterReason(
                    ReasonCode: result.Error.Code,
                    Description: result.Error.Description,
                    OccurredAtUtc: DateTimeOffset.UtcNow);

                try
                {
                    await dlq.ForwardRawToDeadLetterAsync(payload, reason, metadata, effectiveCt).ConfigureAwait(false);
                }
                catch (Exception dlqEx)
                {
                    _logger.LogError(dlqEx, "Failed to forward failed message '{MessageId}' to dead-letter queue", metadata.MessageId);
                }

                return TransportAckResult.DeadLetter;
            }

            if (_options.UnhandledFailureAckResult == TransportAckResult.Ack)
            {
                _logger.LogWarning(
                    "No IDeadLetterQueue registered. Message '{MessageId}' of type '{MessageType}' failed and is being acknowledged (dropped) per UnhandledFailureAckResult=Ack configuration.",
                    metadata.MessageId,
                    metadata.MessageType);
            }

            return _options.UnhandledFailureAckResult;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _cts.IsCancellationRequested)
        {
            _logger.LogInformation("Message processing cancelled during shutdown for {MessageType}", metadata.MessageType);
            return TransportAckResult.NackRequeue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception during message dispatch for {MessageType}", metadata.MessageType);
            return TransportAckResult.NackRequeue;
        }
        finally
        {
            if (Interlocked.Decrement(ref _inFlightCount) == 0)
            {
                lock (_drainLock)
                {
                    _drainTcs?.TrySetResult();
                    _drainTcs = null;
                }
            }
        }
    }

    /// <summary>
    /// Releases the resources used by this instance.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// Asynchronously releases the resources used by this instance.
    /// </summary>
    /// <returns>A value task representing the asynchronous disposal operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException) { }
    }
}




