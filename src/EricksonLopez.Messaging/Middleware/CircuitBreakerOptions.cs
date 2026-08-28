// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.Middleware;

/// <summary>
/// Specifies configuration options for <see cref="CircuitBreakerMiddleware"/>.
/// </summary>
public sealed class CircuitBreakerOptions
{
    /// <summary>
    /// Gets or sets the consecutive failure count threshold required to trip the circuit open.
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Gets or sets the observation window within which consecutive failures are counted toward the threshold.
    /// </summary>
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the duration the circuit remains open before transitioning to the half-open state.
    /// </summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets an optional time provider override used for elapsed-time calculations.
    /// </summary>
    /// <remarks>When <see langword="null"/>, <see cref="TimeProvider.System"/> is used. Override this property in tests to control time progression.</remarks>
    public TimeProvider? TimeProvider { get; set; }
}
