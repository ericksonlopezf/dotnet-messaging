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

/// <summary>
/// Provides message consumption management including endpoint subscriptions, concurrency limits, scoped execution, and graceful shutdown.
/// </summary>
public sealed class MessageConsumer : IMessageConsumer, IAsyncDisposable, IDisposable
{
    private readonly IMessageTransport _transport;
    private readonly IMessageDispatcher _dispatcher;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MessageConsumer> _logger;
    private readonly List<string> _subscribedDestinations;
    private readonly CancellationTokenSource _cts = new();
    private int _inFlightCount;
    private TaskCompletionSource? _drainTcs;
    private readonly object _drainLock = new();
    private bool _isAcceptingMessages = true;
    private bool _started;
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
    /// <exception cref="ArgumentNullException"><paramref name="transport"/>, <paramref name="dispatcher"/>, or <paramref name="scopeFactory"/> is <see langword="null"/></exception>
    public MessageConsumer(
        IMessageTransport transport,
        IMessageDispatcher dispatcher,
        IServiceScopeFactory scopeFactory,
        IEnumerable<string>? subscribedDestinations = null,
        IEnumerable<IHandlerRegistration>? registrations = null,
        ILogger<MessageConsumer>? logger = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? NullLogger<MessageConsumer>.Instance;
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
        if (_started) return;
        _started = true;

        string[] destinations;
        lock (_subscribedDestinations)
        {
            destinations = _subscribedDestinations.ToArray();
        }

        foreach (var destination in destinations)
        {
            var subOptions = new TransportSubscriptionOptions
            {
                MaxConcurrency = Environment.ProcessorCount * 2,
                PrefetchCount = 20
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

            _drainTcs ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
            using var scope = _scopeFactory.CreateScope();
            var result = await _dispatcher.DispatchAsync(
                metadata.MessageType,
                payload,
                metadata,
                scope.ServiceProvider,
                cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                return TransportAckResult.Ack;
            }

            _logger.LogWarning("Message processing returned failure: {Error}", result.Error.Description);
            return TransportAckResult.Ack; // Functional failure is acknowledged; not retried indefinitely
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
                }
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _cts.CancelAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}




