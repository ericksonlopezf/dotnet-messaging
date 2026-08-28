// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Events.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace EricksonLopez.Messaging.Events;

/// <summary>
/// Provides extension methods for registering <see cref="MessagingEventPublisher"/> in an <see cref="IServiceCollection"/>.
/// </summary>
public static class MessagingEventsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="MessagingEventPublisher"/> as the <see cref="IEventPublisher"/> implementation in the service container.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configureOptions">The delegate to configure publisher options, if specified.</param>
    /// <returns>The configured <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddMessagingEventPublisher(
        this IServiceCollection services,
        Action<MessagingEventsOptions>? configureOptions = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services), "Service collection cannot be null.");
        }

        var options = new MessagingEventsOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.AddScoped<IEventPublisher, MessagingEventPublisher>();
        return services;
    }
}
