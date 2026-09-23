// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Tests.Middleware;

using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Middleware;
using EricksonLopez.Messaging.Tests.Common;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Trait("Category", "Unit")]
public class RetryMiddlewareTests
{
    private sealed class TrackingTimeProvider : TimeProvider
    {
        public List<TimeSpan> RecordedDelays { get; } = new();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            RecordedDelays.Add(dueTime);
            callback(state);
            return new DummyTimer();
        }

        private sealed class DummyTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Constructor_Default_Uses100MillisecondsInitialDelay()
    {
        // Arrange
        var timeProvider = new TrackingTimeProvider();
        var middleware = new RetryMiddleware(timeProvider: timeProvider); // default maxRetries = 3, initialDelay = null -> 100ms
        var context = TestMessageContextFactory.CreateContext("orders.retry.v1", "corr-retry");
        int attempts = 0;

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    return ValueTask.FromResult(Result.Failure(Error.Failure("Transient", "Transient")));
                }
                return ValueTask.FromResult(Result.Success());
            },
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        attempts.Should().Be(2);
        // Full-jitter: delay = Random(0, initialDelay * 2^0) = Random(0, 100ms), so in [0ms, 100ms]
        timeProvider.RecordedDelays.Should().ContainSingle()
            .Which.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero)
            .And.BeLessThanOrEqualTo(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task Constructor_CustomInitialDelay_UsesProvidedDelay()
    {
        // Arrange
        var timeProvider = new TrackingTimeProvider();
        var middleware = new RetryMiddleware(maxRetries: 1, initialDelay: TimeSpan.FromMilliseconds(250), timeProvider: timeProvider);
        var context = TestMessageContextFactory.CreateContext("orders.retry.v1", "corr-retry");
        int attempts = 0;

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    return ValueTask.FromResult(Result.Failure(Error.Failure("Transient", "Transient")));
                }
                return ValueTask.FromResult(Result.Success());
            },
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        attempts.Should().Be(2);
        // Full-jitter: delay = Random(0, initialDelay * 2^0) = Random(0, 250ms), so in [0ms, 250ms]
        timeProvider.RecordedDelays.Should().ContainSingle()
            .Which.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero)
            .And.BeLessThanOrEqualTo(TimeSpan.FromMilliseconds(350));
    }

    [Fact]
    public async Task InvokeAsync_SuccessOnFirstAttempt_ExecutesOnce()
    {
        // Arrange
        var middleware = new RetryMiddleware(maxRetries: 3, initialDelay: TimeSpan.FromMilliseconds(1));
        var context = TestMessageContextFactory.CreateContext("orders.retry.v1", "corr-retry");
        int attempts = 0;

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) =>
            {
                attempts++;
                return ValueTask.FromResult(Result.Success());
            },
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_FailureThenSuccess_RetriesAndSucceeds()
    {
        // Arrange
        var middleware = new RetryMiddleware(maxRetries: 3, initialDelay: TimeSpan.FromMilliseconds(1));
        var context = TestMessageContextFactory.CreateContext("orders.retry.v1", "corr-retry");
        int attempts = 0;

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) =>
            {
                attempts++;
                if (attempts < 3)
                {
                    return ValueTask.FromResult(Result.Failure(Error.Failure("Transient.Error", "Transient")));
                }
                return ValueTask.FromResult(Result.Success());
            },
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task InvokeAsync_ThrowsThenSuccess_RetriesAndSucceeds()
    {
        // Arrange
        var middleware = new RetryMiddleware(maxRetries: 3, initialDelay: TimeSpan.FromMilliseconds(1));
        var context = TestMessageContextFactory.CreateContext("orders.retry.v1", "corr-retry");
        int attempts = 0;

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) =>
            {
                attempts++;
                if (attempts < 2)
                {
                    throw new TimeoutException("Database connection timeout");
                }
                return ValueTask.FromResult(Result.Success());
            },
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task InvokeAsync_ExceedsMaxRetriesWithResultFailure_ReturnsFinalFailureResult()
    {
        // Arrange
        var middleware = new RetryMiddleware(maxRetries: 2, initialDelay: TimeSpan.FromMilliseconds(1));
        var context = TestMessageContextFactory.CreateContext("orders.retry.v1", "corr-retry");
        int attempts = 0;
        var persistentError = Error.Validation("Order.Invalid", "Invalid item");

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) =>
            {
                attempts++;
                return ValueTask.FromResult(Result.Failure(persistentError));
            },
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Order.Invalid");
        attempts.Should().Be(3); // 1 initial + 2 retries
    }

    [Fact]
    public async Task InvokeAsync_ExceedsMaxRetriesWithException_RethrowsException()
    {
        // Arrange
        var middleware = new RetryMiddleware(maxRetries: 2, initialDelay: TimeSpan.FromMilliseconds(1));
        var context = TestMessageContextFactory.CreateContext("orders.retry.v1", "corr-retry");
        int attempts = 0;
        var expectedException = new InvalidOperationException("Fatal database down");

        // Act
        Func<Task> act = async () => await middleware.InvokeAsync(
            context,
            (ctx, ct) =>
            {
                attempts++;
                throw expectedException;
            },
            CancellationToken.None);

        // Assert
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(expectedException);
        attempts.Should().Be(3); // 1 initial + 2 retries
    }

    [Fact]
    public async Task Constructor_NegativeMaxRetries_ClampsToZeroAndDoesNotRetry()
    {
        // Arrange
        var middleware = new RetryMiddleware(maxRetries: -5, initialDelay: TimeSpan.FromMilliseconds(1));
        var context = TestMessageContextFactory.CreateContext("orders.retry.v1", "corr-retry");
        int attempts = 0;

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) =>
            {
                attempts++;
                return ValueTask.FromResult(Result.Failure(Error.Failure("Err", "Err")));
            },
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_WhenCancellationRequestedInitially_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var middleware = new RetryMiddleware(maxRetries: 3, initialDelay: TimeSpan.FromMilliseconds(10));
        var context = TestMessageContextFactory.CreateContext("orders.retry.v1", "corr-retry", cancellationToken: cts.Token);

        // Act
        Func<Task> act = async () => await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Success()),
            cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task InvokeAsync_WhenHandlerThrowsOperationCanceledExceptionAndCancellationRequested_RethrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var middleware = new RetryMiddleware(maxRetries: 3, initialDelay: TimeSpan.FromMilliseconds(1));
        var context = TestMessageContextFactory.CreateContext("orders.retry.v1", "corr-retry", cancellationToken: cts.Token);

        // Act
        Func<Task> act = async () => await middleware.InvokeAsync(
            context,
            (ctx, ct) =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            },
            cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task InvokeAsync_WhenCancellationRequestedDuringRetries_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var middleware = new RetryMiddleware(maxRetries: 3, initialDelay: TimeSpan.FromMilliseconds(10));
        var context = TestMessageContextFactory.CreateContext("orders.retry.v1", "corr-retry", cancellationToken: cts.Token);
        int attempts = 0;

        // Act
        Func<Task> act = async () => await middleware.InvokeAsync(
            context,
            (ctx, ct) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    cts.Cancel();
                    return ValueTask.FromResult(Result.Failure(Error.Failure("Transient", "Transient")));
                }
                return ValueTask.FromResult(Result.Success());
            },
            cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task InvokeAsync_ExponentialBackoff_DelaysAreWithinJitteredBounds()
    {
        // Arrange
        var timeProvider = new TrackingTimeProvider();
        var middleware = new RetryMiddleware(maxRetries: 2, initialDelay: TimeSpan.FromMilliseconds(100), timeProvider: timeProvider);
        var context = TestMessageContextFactory.CreateContext("orders.retry.v1", "corr-retry");

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Transient", "Transient"))),
            CancellationToken.None);

        // Assert
        // Full-jitter delays: Random(0, 100ms * 2^attempt) per attempt
        // Attempt 0: ceiling = 100ms * 1 = 100ms → delay in [0, 100ms]
        // Attempt 1: ceiling = 100ms * 2 = 200ms → delay in [0, 200ms]
        result.IsFailure.Should().BeTrue();
        timeProvider.RecordedDelays.Should().HaveCount(2);
        timeProvider.RecordedDelays[0].Should().BeLessThanOrEqualTo(TimeSpan.FromMilliseconds(100) + TimeSpan.FromMilliseconds(100));
        timeProvider.RecordedDelays[0].Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        timeProvider.RecordedDelays[1].Should().BeLessThanOrEqualTo(TimeSpan.FromMilliseconds(200) + TimeSpan.FromMilliseconds(100));
        timeProvider.RecordedDelays[1].Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task InvokeAsync_ThreeRetries_DelaysAreWithinJitteredBounds()
    {
        // Arrange
        var timeProvider = new TrackingTimeProvider();
        var middleware = new RetryMiddleware(maxRetries: 3, initialDelay: TimeSpan.FromMilliseconds(50), timeProvider: timeProvider);
        var context = TestMessageContextFactory.CreateContext("orders.retry.v1", "corr-retry");

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Transient", "Transient"))),
            CancellationToken.None);

        // Assert
        // Full-jitter delays: Random(0, 50ms * 2^attempt) + jitter(0..100ms) per attempt
        // Attempt 0: base ceiling = 50ms * 1 → total delay max = 150ms
        // Attempt 1: base ceiling = 50ms * 2 = 100ms → total delay max = 200ms
        // Attempt 2: base ceiling = 50ms * 4 = 200ms → total delay max = 300ms
        result.IsFailure.Should().BeTrue();
        timeProvider.RecordedDelays.Should().HaveCount(3);
        // All delays must be >= 0ms
        timeProvider.RecordedDelays.Should().AllSatisfy(d => d.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero));
    }

    [Fact]
    public async Task InvokeAsync_ShouldRetryFalse_ReturnsImmediatelyWithoutRetrying()
    {
        // Arrange
        var timeProvider = new TrackingTimeProvider();
        var options = new RetryOptions
        {
            MaxRetries = 3,
            InitialDelay = TimeSpan.FromMilliseconds(50),
            TimeProvider = timeProvider,
            ShouldRetry = err => err.Type != ErrorType.Validation
        };
        var middleware = new RetryMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("orders.retry.v1", "corr-retry");
        int attempts = 0;

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) =>
            {
                attempts++;
                return ValueTask.FromResult(Result.Failure(Error.Validation("Test.Validation", "Invalid payload")));
            },
            CancellationToken.None);

        // Assert: should exit on attempt 1 without retrying or delaying
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Test.Validation");
        attempts.Should().Be(1);
        timeProvider.RecordedDelays.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_ShouldRetryTrue_RetriesUpToMax()
    {
        // Arrange
        var timeProvider = new TrackingTimeProvider();
        var options = new RetryOptions
        {
            MaxRetries = 3,
            InitialDelay = TimeSpan.FromMilliseconds(50),
            TimeProvider = timeProvider,
            ShouldRetry = err => err.Type != ErrorType.Validation
        };
        var middleware = new RetryMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("orders.retry.v1", "corr-retry");
        int attempts = 0;

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) =>
            {
                attempts++;
                return ValueTask.FromResult(Result.Failure(Error.Failure("Database.Transient", "Timeout")));
            },
            CancellationToken.None);

        // Assert: 1 initial attempt + 3 retries = 4 attempts total
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Database.Transient");
        attempts.Should().Be(4);
        timeProvider.RecordedDelays.Should().HaveCount(3);
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        Action act = () => new RetryMiddleware((RetryOptions)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task InvokeAsync_CustomMaxDelay_CapsDelayCeiling()
    {
        var timeProvider = new TrackingTimeProvider();
        var maxDelay = TimeSpan.FromMilliseconds(50);
        var middleware = new RetryMiddleware(
            maxRetries: 4,
            initialDelay: TimeSpan.FromMilliseconds(100),
            timeProvider: timeProvider,
            maxDelay: maxDelay);
        var context = TestMessageContextFactory.CreateContext("orders.retry.v1", "corr-retry");

        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err", "Err"))),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        timeProvider.RecordedDelays.Should().HaveCount(4);
        timeProvider.RecordedDelays.Should().AllSatisfy(d =>
        {
            d.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
            d.Should().BeLessThanOrEqualTo(maxDelay);
        });
    }

    [Fact]
    public void Constructor_ExplicitInitialDelay_SetsInitialDelayField()
    {
        var expected = TimeSpan.FromSeconds(5);
        var middleware = new RetryMiddleware(initialDelay: expected);

        var field = typeof(RetryMiddleware).GetField("_initialDelay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field!.GetValue(middleware).Should().Be(expected);
    }

    [Fact]
    public async Task InvokeAsync_FullJitterExponentialBackoff_CalculatesExponentialDelayGreaterThanOneMillisecond()
    {
        var initialDelay = TimeSpan.FromSeconds(10);
        bool observedGreaterThanDivisorCeiling = false;

        for (int i = 0; i < 20; i++)
        {
            var timeProvider = new TrackingTimeProvider();
            var middleware = new RetryMiddleware(
                maxRetries: 3,
                initialDelay: initialDelay,
                timeProvider: timeProvider,
                maxDelay: TimeSpan.FromMinutes(10));
            var context = TestMessageContextFactory.CreateContext("orders.retry.v1", "corr-retry");

            var result = await middleware.InvokeAsync(
                context,
                (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err", "Err"))),
                CancellationToken.None);

            result.IsFailure.Should().BeTrue();
            timeProvider.RecordedDelays.Should().HaveCount(3);

            // On attempt = 2 (index 2), shift = 2:
            // True code ceiling: 10,000 * 4 = 40,000ms.
            // Under / (1L << shift) mutant, ceiling is 10,000 / 4 = 2,500ms (can never exceed 2,500ms).
            // Under >> shift or >>> shift mutants, ceiling is 0 (jittered to 1ms).
            if (timeProvider.RecordedDelays[2].TotalMilliseconds > 2600)
            {
                observedGreaterThanDivisorCeiling = true;
                break;
            }
        }

        observedGreaterThanDivisorCeiling.Should().BeTrue();
    }
}




