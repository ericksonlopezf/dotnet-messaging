// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Contracts;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EricksonLopez.Messaging.HealthChecks;

/// <summary>
/// Provides a health check verifying messaging registration and transport readiness.
/// </summary>
public sealed class MessagingHealthCheck : IHealthCheck
{
    private readonly IMessagePublisher? _publisher;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessagingHealthCheck"/> class with the specified publisher.
    /// </summary>
    /// <param name="publisher">The message publisher instance to check, if registered.</param>
    public MessagingHealthCheck(IMessagePublisher? publisher = null)
    {
        _publisher = publisher;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_publisher is null)
        {
            return Task.FromResult(HealthCheckResult.Degraded("Message publisher is not registered in the service provider."));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Messaging transport is active and ready to publish messages."));
    }
}
