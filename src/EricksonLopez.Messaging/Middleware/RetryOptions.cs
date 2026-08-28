// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.Middleware;

/// <summary>
/// Specifies configuration options for <see cref="RetryMiddleware"/>.
/// </summary>
public sealed class RetryOptions
{
    /// <summary>
    /// Gets or sets the maximum number of retry attempts before the final attempt is allowed to propagate.
    /// Values less than zero are clamped to zero, meaning no retries occur.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay applied before the first retry attempt. Subsequent delays are calculated
    /// using an exponential backoff formula with full-jitter randomization.
    /// </summary>
    /// <remarks>Defaults to 100 milliseconds.</remarks>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets or sets an optional time provider override used for delay scheduling.
    /// </summary>
    /// <remarks>When <see langword="null"/>, <see cref="TimeProvider.System"/> is used. Override this property in tests to control time progression.</remarks>
    public TimeProvider? TimeProvider { get; set; }
}
