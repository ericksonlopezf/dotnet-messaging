// Copyright © Erickson Lopez. MIT License.
using System;

namespace Microsoft.Extensions.DependencyInjection;

using EricksonLopez.Messaging.Transport;
using EricksonLopez.Messaging.Transport.RabbitMQ;

/// <summary>
/// Provides extension methods for configuring RabbitMQ messaging transport in an <see cref="IServiceCollection"/>.
/// </summary>
public static class RabbitMqMessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers RabbitMQ as the messaging transport in the service container.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">The delegate to configure RabbitMQ transport options, if specified.</param>
    /// <returns>The configured <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddRabbitMqMessagingTransport(
        this IServiceCollection services,
        Action<RabbitMqTransportOptions>? configure = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services), "Service collection cannot be null.");
        }

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddSingleton<IMessageTransport, RabbitMqMessageTransport>();
        return services;
    }
}


