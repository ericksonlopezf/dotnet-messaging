// Copyright © Erickson Lopez. MIT License.
namespace OpenTelemetry.Trace;

using System;
using EricksonLopez.Messaging.Diagnostics;

/// <summary>
/// Provides extension methods for registering messaging instrumentation with OpenTelemetry <see cref="TracerProviderBuilder"/>.
/// </summary>
public static class MessagingTracerProviderBuilderExtensions
{
    /// <summary>
    /// Adds the messaging activity source to the OpenTelemetry tracer provider.
    /// </summary>
    /// <param name="builder">The tracer provider builder.</param>
    /// <returns>The configured <see cref="TracerProviderBuilder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/></exception>
    public static TracerProviderBuilder AddMessagingInstrumentation(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddSource(MessagingDiagnostics.ActivitySourceName);
    }
}
