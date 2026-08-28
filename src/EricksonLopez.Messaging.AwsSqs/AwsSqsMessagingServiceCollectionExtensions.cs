// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Messaging.Transport.AwsSqs;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods for configuring AWS SQS messaging transport in an <see cref="IServiceCollection"/>.
/// </summary>
public static class AwsSqsMessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers AWS SQS as the messaging transport in the service container.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">The delegate to configure AWS SQS transport options, if specified.</param>
    /// <returns>The configured <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddAwsSqsMessagingTransport(
        this IServiceCollection services,
        Action<AwsSqsTransportOptions>? configure = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services), "Service collection cannot be null.");
        }

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddSingleton<IMessageTransport, AwsSqsMessageTransport>();
        return services;
    }
}
