// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Events.Contracts;
using EricksonLopez.Messaging.Contracts;

namespace EricksonLopez.Messaging.Events;

/// <summary>
/// Dispatches domain and integration events through an <see cref="IMessagePublisher"/>.
/// </summary>
public sealed class MessagingEventPublisher : IEventPublisher
{
    private readonly IMessagePublisher _messagePublisher;
    private readonly MessagingEventsOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessagingEventPublisher"/> class with the specified message publisher and options.
    /// </summary>
    /// <param name="messagePublisher">The message publisher implementation.</param>
    /// <param name="options">The publisher configuration options, if specified.</param>
    /// <exception cref="ArgumentNullException"><paramref name="messagePublisher"/> is <see langword="null"/></exception>
    public MessagingEventPublisher(
        IMessagePublisher messagePublisher,
        MessagingEventsOptions? options = null)
    {
        _messagePublisher = messagePublisher ?? throw new ArgumentNullException(nameof(messagePublisher));
        _options = options ?? new MessagingEventsOptions();
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="eventInstance"/> is <see langword="null"/></exception>
    /// <exception cref="InvalidOperationException">
    /// Publishing via the underlying message transport failed and <see cref="MessagingEventsOptions.ThrowOnFailure"/> is <see langword="true"/>
    /// </exception>
    public async ValueTask PublishAsync<TEvent>(
        TEvent eventInstance,
        CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(eventInstance);

        var publishOptions = new MessagePublishOptions
        {
            Destination = _options.DestinationResolver?.Invoke(typeof(TEvent))
        };

        var result = await _messagePublisher.PublishAsync(
            eventInstance,
            publishOptions,
            cancellationToken);

        if (result.IsFailure && _options.ThrowOnFailure)
        {
            throw new InvalidOperationException(
                $"Failed to publish event '{typeof(TEvent).Name}' ({eventInstance.Id}) via messaging publisher. Error: {result.Error}");
        }
    }
}
