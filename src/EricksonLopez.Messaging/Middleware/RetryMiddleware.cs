// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Middleware;

using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;

/// <summary>
/// Provides retry middleware with exponential backoff and full-jitter randomization for transient processing failures.
/// </summary>
/// <remarks>
/// <para>
/// Implements the "full jitter" exponential backoff strategy: <c>delay = Random(0, initialDelay * 2^attempt)</c>.
/// This prevents thundering-herd scenarios where many consumers retry simultaneously after a shared downstream failure.
/// </para>
/// <para>
/// If cancellation is requested at any point, the retry loop exits immediately without scheduling further delays.
/// The last retry attempt (when maximum retry attempts have been exhausted) is allowed
/// to propagate exceptions and failures to the calling pipeline.
/// </para>
/// </remarks>
public sealed class RetryMiddleware : IMessageMiddleware
{
    private readonly int _maxRetries;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _maxDelay;
    private readonly TimeProvider _timeProvider;
    private readonly Func<Error, bool>? _shouldRetry;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryMiddleware"/> class with the specified retry parameters.
    /// </summary>
    /// <param name="maxRetries">The maximum number of retry attempts before the last attempt is allowed to propagate. Values less than zero are clamped to zero, meaning no retries occur.</param>
    /// <param name="initialDelay">The base delay used as the ceiling for full-jitter calculation. The ceiling doubles on each attempt. Defaults to 100 milliseconds when <see langword="null"/>.</param>
    /// <param name="timeProvider">An optional time provider used for delay scheduling. When <see langword="null"/>, <see cref="TimeProvider.System"/> is used.</param>
    /// <param name="maxDelay">The maximum allowable delay ceiling between attempts. Defaults to 1 minute when <see langword="null"/>.</param>
    /// <param name="shouldRetry">An optional predicate to determine whether a failed <see cref="Result"/> should be retried.</param>
    public RetryMiddleware(
        int maxRetries = 3,
        TimeSpan? initialDelay = null,
        TimeProvider? timeProvider = null,
        TimeSpan? maxDelay = null,
        Func<Error, bool>? shouldRetry = null)
    {
        _maxRetries = Math.Max(0, maxRetries);
        _initialDelay = initialDelay ?? TimeSpan.FromMilliseconds(100);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maxDelay = maxDelay ?? TimeSpan.FromMinutes(1);
        _shouldRetry = shouldRetry;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryMiddleware"/> class with the specified <see cref="RetryOptions"/>.
    /// </summary>
    /// <param name="options">The configuration options for the retry logic.</param>
    /// <remarks>
    /// Due to C# constructor chaining semantics, the <c>this(...)</c> delegation executes before the constructor body.
    /// If <paramref name="options"/> is <see langword="null"/>, each property access uses <c>null-coalescing</c> operators
    /// to fall back to safe defaults during chaining. <c>ArgumentNullException.ThrowIfNull</c> then validates
    /// and throws in the constructor body, ensuring the exception is correctly propagated without leaving
    /// the object in a corrupted state — the chaining is fully defensive.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/></exception>
    public RetryMiddleware(RetryOptions options)
        : this(options?.MaxRetries ?? 3, options?.InitialDelay, options?.TimeProvider, options?.MaxDelay, options?.ShouldRetry)
    {
        ArgumentNullException.ThrowIfNull(options);
    }

    /// <inheritdoc />
    public async ValueTask<Result> InvokeAsync(
        MessageContext context,
        MessageExecutionDelegate next,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        for (int attempt = 0; attempt < _maxRetries; attempt++)
        {
            try
            {
                var result = await next(context, cancellationToken);
                if (result.IsSuccess)
                {
                    return result;
                }

                if (_shouldRetry is not null && !_shouldRetry(result.Error))
                {
                    return result;
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Fallthrough to jitter-delayed retry
            }

            // Full-jitter exponential backoff: delay = Random(0, initialDelay * 2^attempt)
            // Clamp attempt to 30 to prevent 32-bit signed shift arithmetic overflow (1 << 31 < 0)
            int shift = Math.Min(attempt, 30);
            double calculatedCeiling = _initialDelay.TotalMilliseconds * (1L << shift);
            double ceilingMs = Math.Min(_maxDelay.TotalMilliseconds, calculatedCeiling);
            var jitteredMs = Math.Max(1.0, Random.Shared.NextDouble() * ceilingMs);
            var delay = TimeSpan.FromMilliseconds(jitteredMs);
            await Task.Delay(delay, _timeProvider, cancellationToken);
        }

        return await next(context, cancellationToken);
    }
}
