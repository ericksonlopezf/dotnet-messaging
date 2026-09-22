// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Messaging.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EricksonLopez.Messaging;

/// <summary>
/// Provides extension methods for registering <see cref="MessagingHealthCheck"/> in health check services.
/// </summary>
public static class MessagingHealthCheckExtensions
{
    /// <summary>
    /// Registers a transient <see cref="MessagingHealthCheck"/> in the service collection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The configured <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddMessagingHealthCheck(this IServiceCollection services)
    {
        services.AddTransient<IHealthCheck, MessagingHealthCheck>();
        return services;
    }
}
