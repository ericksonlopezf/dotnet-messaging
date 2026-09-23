// Copyright © Erickson Lopez. MIT License.
namespace EricksonLopez.Messaging.Middleware;

using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

/// <summary>
/// Provides circuit breaker resilience middleware to halt message processing when failure thresholds are breached.
/// </summary>
/// <remarks>
/// <para>
/// Implements a three-state machine: <c>Closed</c> (normal), <c>Open</c> (fast-fail), and <c>HalfOpen</c> (recovery probe).
/// </para>
/// <para>
/// Failures are counted within a sliding observation window defined by <see cref="CircuitBreakerOptions.SamplingDuration"/>.
/// If the time elapsed since the first recorded consecutive failure exceeds <see cref="CircuitBreakerOptions.SamplingDuration"/>,
/// the failure counter is reset before counting the new failure, preventing stale failures from accumulating across
/// long idle periods. A successful execution always resets the failure counter to zero.
/// </para>
/// </remarks>
public sealed class CircuitBreakerMiddleware : IMessageMiddleware
{
    private readonly CircuitBreakerOptions _options;
    private readonly ILogger<CircuitBreakerMiddleware> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly object _lock = new();

    private enum State
    {
        Closed,
        Open,
        HalfOpen
    }

    private volatile State _state = State.Closed;
    private int _consecutiveFailures;
    private long _openedTimestamp;
    private long _firstFailureTimestamp;

    /// <summary>
    /// Initializes a new instance of the <see cref="CircuitBreakerMiddleware"/> class with the specified options and logger.
    /// </summary>
    /// <param name="options">The circuit breaker configuration options.</param>
    /// <param name="logger">The logger instance, if specified.</param>
    public CircuitBreakerMiddleware(
        IOptions<CircuitBreakerOptions>? options = null,
        ILogger<CircuitBreakerMiddleware>? logger = null)
    {
        _options = options?.Value ?? new CircuitBreakerOptions();
        _logger = logger ?? NullLogger<CircuitBreakerMiddleware>.Instance;
        _timeProvider = _options.TimeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CircuitBreakerMiddleware"/> class with explicit options.
    /// </summary>
    /// <param name="options">The circuit breaker configuration options.</param>
    /// <param name="logger">The logger instance, if specified.</param>
    public CircuitBreakerMiddleware(
        CircuitBreakerOptions options,
        ILogger<CircuitBreakerMiddleware>? logger = null)
        : this(Options.Create(options), logger)
    {
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="next"/> is <see langword="null"/></exception>
    public async ValueTask<Result> InvokeAsync(
        MessageContext context,
        MessageExecutionDelegate next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        cancellationToken.ThrowIfCancellationRequested();

        if (_state == State.Open)
        {
            lock (_lock)
            {
                if (_state == State.Open)
                {
                    var elapsed = _timeProvider.GetElapsedTime(_openedTimestamp);
                    if (elapsed >= _options.BreakDuration)
                    {
                        _state = State.HalfOpen;
                        _logger.LogInformation("Circuit breaker transitioned from OPEN to HALF-OPEN.");
                    }
                    else
                    {
                        return Result.Failure(Error.Failure(
                            code: "Messaging.CircuitBreaker.Open",
                            description: "Circuit breaker is OPEN. Message processing is temporarily halted."));
                    }
                }
            }
        }

        Result result = default;
        try
        {
            result = await next(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            RecordFailure();
            throw;
        }

        if (result.IsSuccess)
        {
            RecordSuccess();
        }
        else if (_options.FailureFilter is null || _options.FailureFilter(result.Error))
        {
            RecordFailure();
        }

        return result;
    }

    /// <remarks>
    /// A successful execution always resets <c>_consecutiveFailures</c> to zero, regardless of prior failure count.
    /// This means that a single success in <see cref="State.Closed"/> or <see cref="State.HalfOpen"/> state
    /// clears all previously accumulated failure history.
    /// </remarks>
    private void RecordSuccess()
    {
        if (_state == State.Closed && Volatile.Read(ref _consecutiveFailures) == 0)
        {
            return;
        }

        lock (_lock)
        {
            if (_state == State.HalfOpen)
            {
                _state = State.Closed;
                _consecutiveFailures = 0;
                _firstFailureTimestamp = 0;
                _logger.LogInformation("Circuit breaker recovered: transitioned from HALF-OPEN to CLOSED.");
            }
            else if (_state == State.Closed)
            {
                _consecutiveFailures = 0;
                _firstFailureTimestamp = 0;
            }
        }
    }

    private void RecordFailure()
    {
        lock (_lock)
        {
            var now = _timeProvider.GetTimestamp();

            // Sliding window: if the observation window has expired since the first consecutive failure,
            // reset the counter before incrementing. This implements SamplingDuration semantics:
            // only failures within the most recent SamplingDuration window count toward the threshold.
            if (_firstFailureTimestamp != 0)
            {
                var windowElapsed = _timeProvider.GetElapsedTime(_firstFailureTimestamp);
                if (windowElapsed >= _options.SamplingDuration)
                {
                    _consecutiveFailures = 0;
                    _firstFailureTimestamp = 0;
                }
            }

            if (_consecutiveFailures == 0)
            {
                _firstFailureTimestamp = now;
            }

            _consecutiveFailures++;
            if (_state == State.HalfOpen || _consecutiveFailures >= _options.FailureThreshold)
            {
                _state = State.Open;
                _openedTimestamp = now;
                _logger.LogWarning(
                    "Circuit breaker TRIPPED OPEN. Failure threshold breached ({Failures} consecutive failures within {Window}).",
                    _consecutiveFailures,
                    _options.SamplingDuration);
            }
        }
    }
}
