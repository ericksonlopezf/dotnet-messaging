// Copyright © Erickson Lopez. MIT License.
using System;

namespace Microsoft.Extensions.DependencyInjection;

using EricksonLopez.Messaging.Transport;
using EricksonLopez.Messaging.Transport.AzureServiceBus;

/// <summary>
/// Provides extension methods for configuring Azure Service Bus transport in an <see cref="IServiceCollection"/>.
/// </summary>
public static class AzureServiceBusMessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers Azure Service Bus as the messaging transport in the service container.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">The delegate to configure Azure Service Bus transport options, if specified.</param>
    /// <returns>The configured <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddAzureServiceBusMessagingTransport(
        this IServiceCollection services,
        Action<AzureServiceBusTransportOptions>? configure = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services), "Service collection cannot be null.");
        }

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddSingleton<IMessageTransport, AzureServiceBusMessageTransport>();
        return services;
    }
}


