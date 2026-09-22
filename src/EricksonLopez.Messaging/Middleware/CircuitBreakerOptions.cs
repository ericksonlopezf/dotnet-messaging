// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Result;

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
    /// <remarks>
    /// When a new failure is recorded, if the time elapsed since the first consecutive failure in the current
    /// sequence exceeds <see cref="SamplingDuration"/>, the failure counter is reset before incrementing.
    /// This prevents failures that are spread far apart in time from accumulating toward
    /// <see cref="FailureThreshold"/>. Defaults to 30 seconds.
    /// </remarks>
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

    /// <summary>
    /// Gets or sets an optional predicate to determine whether an error should be considered a failure for the circuit breaker.
    /// When <see langword="null"/>, all <see cref="Result"/> failures are treated as failures.
    /// </summary>
    public Func<Error, bool>? FailureFilter { get; set; }
}
