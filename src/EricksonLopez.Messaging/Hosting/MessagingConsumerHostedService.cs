// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Hosting;

using EricksonLopez.Messaging.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Provides a hosted service managing the background lifecycle, startup, and graceful shutdown of an <see cref="IMessageConsumer"/>.
/// </summary>
public sealed class MessagingConsumerHostedService : BackgroundService
{
    private readonly IMessageConsumer _consumer;
    private readonly ILogger<MessagingConsumerHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessagingConsumerHostedService"/> class with the specified consumer and logger.
    /// </summary>
    /// <param name="consumer">The message consumer lifecycle manager.</param>
    /// <param name="logger">The logger instance, if specified.</param>
    /// <exception cref="ArgumentNullException"><paramref name="consumer"/> is <see langword="null"/></exception>
    public MessagingConsumerHostedService(
        IMessageConsumer consumer,
        ILogger<MessagingConsumerHostedService>? logger = null)
    {
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        _logger = logger ?? NullLogger<MessagingConsumerHostedService>.Instance;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting MessagingConsumerHostedService...");
        await _consumer.StartAsync(stoppingToken).ConfigureAwait(false);

        // Keep service alive until stoppingToken fires
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = stoppingToken.Register(() => tcs.TrySetResult());
        await tcs.Task.ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Executes a graceful shutdown sequence: stops accepting new messages via
    /// <see cref="IMessageConsumer.StopReceivingAsync"/>, drains all in-flight messages
    /// via <see cref="IMessageConsumer.DrainInFlightMessagesAsync"/>, then delegates
    /// to the base <see cref="BackgroundService.StopAsync"/> implementation.
    /// </remarks>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping MessagingConsumerHostedService...");
        await _consumer.StopReceivingAsync().ConfigureAwait(false);
        await _consumer.DrainInFlightMessagesAsync(cancellationToken).ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}



