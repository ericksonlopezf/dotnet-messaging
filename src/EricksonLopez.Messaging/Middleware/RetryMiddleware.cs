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
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryMiddleware"/> class with the specified retry parameters.
    /// </summary>
    /// <param name="maxRetries">The maximum number of retry attempts before the last attempt is allowed to propagate. Values less than zero are clamped to zero, meaning no retries occur.</param>
    /// <param name="initialDelay">The base delay used as the ceiling for full-jitter calculation. The ceiling doubles on each attempt. Defaults to 100 milliseconds when <see langword="null"/>.</param>
    /// <param name="timeProvider">An optional time provider used for delay scheduling. When <see langword="null"/>, <see cref="TimeProvider.System"/> is used.</param>
    public RetryMiddleware(int maxRetries = 3, TimeSpan? initialDelay = null, TimeProvider? timeProvider = null)
    {
        _maxRetries = Math.Max(0, maxRetries);
        _initialDelay = initialDelay ?? TimeSpan.FromMilliseconds(100);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryMiddleware"/> class with the specified <see cref="RetryOptions"/>.
    /// </summary>
    /// <param name="options">The configuration options for the retry logic.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/></exception>
    public RetryMiddleware(RetryOptions options)
        : this(options?.MaxRetries ?? 3, options?.InitialDelay, options?.TimeProvider)
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
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Fallthrough to jitter-delayed retry
            }

            // Full-jitter exponential backoff: delay = Random(0, initialDelay * 2^attempt)
            var ceilingMs = _initialDelay.TotalMilliseconds * (1 << attempt);
            var jitteredMs = Random.Shared.NextDouble() * ceilingMs;
            var delay = TimeSpan.FromMilliseconds(jitteredMs);
            await Task.Delay(delay, _timeProvider, cancellationToken);
        }

        return await next(context, cancellationToken);
    }
}
