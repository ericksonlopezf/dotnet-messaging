// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.Middleware;

/// <summary>
/// Specifies configuration options for <see cref="HandlerTimeoutMiddleware"/>.
/// </summary>
public sealed class HandlerTimeoutOptions
{
    /// <summary>
    /// Gets or sets the maximum execution duration allowed for message processing before cancelling the operation.
    /// </summary>
    /// <remarks>Defaults to 30 seconds. Must be a positive, finite duration when set explicitly
    /// via <c>MessagingOptionsBuilder.AddHandlerTimeout(TimeSpan)</c>.</remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets an optional time provider override used for timeout cancellation scheduling.
    /// </summary>
    /// <remarks>When <see langword="null"/>, <see cref="TimeProvider.System"/> is used. Override this property in tests to control time progression.</remarks>
    public TimeProvider? TimeProvider { get; set; }
}
