// Copyright © Erickson Lopez. MIT License.
namespace OpenTelemetry.Metrics;

using System;
using EricksonLopez.Messaging.Diagnostics;

/// <summary>
/// Provides extension methods for registering messaging instrumentation with OpenTelemetry <see cref="MeterProviderBuilder"/>.
/// </summary>
public static class MessagingMeterProviderBuilderExtensions
{
    /// <summary>
    /// Adds the messaging meter to the OpenTelemetry meter provider.
    /// </summary>
    /// <param name="builder">The meter provider builder.</param>
    /// <returns>The configured <see cref="MeterProviderBuilder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/></exception>
    public static MeterProviderBuilder AddMessagingInstrumentation(this MeterProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddMeter(MessagingDiagnostics.MeterName);
    }
}
