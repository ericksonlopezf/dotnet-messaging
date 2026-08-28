// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Contracts;


/// <summary>
/// Defines a message consumer lifecycle manager responsible for subscription, ingestion, and graceful shutdown.
/// </summary>
public interface IMessageConsumer
{
    /// <summary>
    /// Starts consuming messages from configured transport channels.
    /// </summary>
    /// <param name="cancellationToken">A token that can be used to cancel the startup operation.</param>
    /// <returns>A value task representing the asynchronous operation.</returns>
    ValueTask StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops accepting new messages from the underlying transport during application shutdown.
    /// </summary>
    /// <returns>A value task representing the asynchronous operation.</returns>
    ValueTask StopReceivingAsync();

    /// <summary>
    /// Drains in-flight messages currently processing in handler pipelines.
    /// </summary>
    /// <param name="cancellationToken">A token that can be used to cancel the drain operation.</param>
    /// <returns>A value task representing the asynchronous operation.</returns>
    ValueTask DrainInFlightMessagesAsync(CancellationToken cancellationToken = default);
}



