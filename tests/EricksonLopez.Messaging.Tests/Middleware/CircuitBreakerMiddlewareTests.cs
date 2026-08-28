// Copyright © Erickson Lopez. MIT License.
namespace EricksonLopez.Messaging.Tests.Middleware;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Middleware;
using EricksonLopez.Messaging.Tests.Common;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using Result = EricksonLopez.Result.Result;

[Trait("Category", "Unit")]
public class CircuitBreakerMiddlewareTests
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp = 1_000_000_000L;

        public void Advance(TimeSpan duration)
        {
            _timestamp += (long)(duration.TotalSeconds * TimestampFrequency);
        }

        public override long GetTimestamp() => _timestamp;
    }

    [Fact]
    public async Task InvokeAsync_SuccessfulCalls_RemainsClosedAndReturnsSuccess()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            BreakDuration = TimeSpan.FromSeconds(10),
            TimeProvider = timeProvider
        };
        var middleware = new CircuitBreakerMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("test.circuit", "corr-1");

        // Act
        var result1 = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);
        var result2 = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);

        // Assert
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_FailuresReachThreshold_TripsToOpenAndRejectsFast()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            BreakDuration = TimeSpan.FromSeconds(10),
            TimeProvider = timeProvider
        };
        var middleware = new CircuitBreakerMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("test.circuit", "corr-1");

        // 2 failures
        await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err1", "Failure 1"))), CancellationToken.None);
        await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err2", "Failure 2"))), CancellationToken.None);

        // 3rd call should be rejected fast without calling next
        bool nextCalled = false;
        var result = await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            nextCalled = true;
            return ValueTask.FromResult(Result.Success());
        }, CancellationToken.None);

        // Assert
        nextCalled.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Messaging.CircuitBreaker.Open");
        result.Error.Description.Should().Be("Circuit breaker is OPEN. Message processing is temporarily halted.");
    }

    [Fact]
    public async Task InvokeAsync_FailureFollowedBySuccess_ResetsFailureCounterInClosedState()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            BreakDuration = TimeSpan.FromSeconds(10),
            TimeProvider = timeProvider
        };
        var middleware = new CircuitBreakerMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("test.circuit", "corr-1");

        // 1 failure
        var r1 = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err1", "Fail 1"))), CancellationToken.None);
        r1.IsFailure.Should().BeTrue();

        // 1 success -> should reset consecutive failures to 0
        var r2 = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);
        r2.IsSuccess.Should().BeTrue();

        // 1 failure -> should NOT trip breaker since count was reset
        var r3 = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err2", "Fail 2"))), CancellationToken.None);
        r3.IsFailure.Should().BeTrue();

        // Breaker should still be closed and execute next
        bool nextExecuted = false;
        var r4 = await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            nextExecuted = true;
            return ValueTask.FromResult(Result.Success());
        }, CancellationToken.None);

        nextExecuted.Should().BeTrue();
        r4.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_CancellationExceptionDuringNext_DoesNotIncrementFailureCount()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            BreakDuration = TimeSpan.FromSeconds(10),
            TimeProvider = timeProvider
        };
        var middleware = new CircuitBreakerMiddleware(options);
        using var cts = new CancellationTokenSource();
        var context = TestMessageContextFactory.CreateContext("test.circuit", "corr-1", cancellationToken: cts.Token);

        Func<Task> act = async () => await middleware.InvokeAsync(
            context,
            (ctx, ct) =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            },
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        // Breaker must NOT have tripped open because it was cancelled
        bool nextCalled = false;
        var result = await middleware.InvokeAsync(
            TestMessageContextFactory.CreateContext("test.circuit", "corr-2"),
            (ctx, ct) =>
            {
                nextCalled = true;
                return ValueTask.FromResult(Result.Success());
            },
            CancellationToken.None);

        nextCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_AfterBreakDuration_TransitionsToHalfOpenAndRecoversOnSuccess()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            BreakDuration = TimeSpan.FromSeconds(10),
            TimeProvider = timeProvider
        };
        var middleware = new CircuitBreakerMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("test.circuit", "corr-1");

        // Trip open
        await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err1", "Failure 1"))), CancellationToken.None);
        await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err2", "Failure 2"))), CancellationToken.None);

        // Advance time by exact break duration
        timeProvider.Advance(options.BreakDuration);

        // Trial call succeeds
        var halfOpenResult = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);

        // Assert trial call succeeded
        halfOpenResult.IsSuccess.Should().BeTrue();

        // 1 subsequent failure in Closed state should NOT trip breaker (threshold is 2)
        var singleFailResult = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("ErrX", "Fail X"))), CancellationToken.None);
        singleFailResult.IsFailure.Should().BeTrue();

        // Subsequent call in Closed state also succeeds and executes next
        bool nextCalled = false;
        var closedResult = await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            nextCalled = true;
            return ValueTask.FromResult(Result.Success());
        }, CancellationToken.None);
        nextCalled.Should().BeTrue();
        closedResult.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ExactBreakDuration_TransitionsToHalfOpen()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            BreakDuration = TimeSpan.FromSeconds(10),
            TimeProvider = timeProvider
        };
        var middleware = new CircuitBreakerMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("test.circuit", "corr-1");

        // Trip open
        await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err1", "Failure 1"))), CancellationToken.None);

        // Advance by exact break duration
        timeProvider.Advance(options.BreakDuration);

        // Trial call succeeds
        var result = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_HalfOpenFails_TripsBackToOpen()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            BreakDuration = TimeSpan.FromSeconds(10),
            TimeProvider = timeProvider
        };
        var middleware = new CircuitBreakerMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("test.circuit", "corr-1");

        // Trip open
        await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err1", "Failure 1"))), CancellationToken.None);
        await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err2", "Failure 2"))), CancellationToken.None);

        // Advance time past break duration -> HalfOpen
        timeProvider.Advance(TimeSpan.FromSeconds(11));

        // Trial call fails
        var trialResult = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("ErrTrial", "Trial fail"))), CancellationToken.None);
        trialResult.IsFailure.Should().BeTrue();

        // Next call immediately rejected again
        bool nextCalled = false;
        var rejectedResult = await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            nextCalled = true;
            return ValueTask.FromResult(Result.Success());
        }, CancellationToken.None);

        nextCalled.Should().BeFalse();
        rejectedResult.Error.Code.Should().Be("Messaging.CircuitBreaker.Open");
    }

    [Fact]
    public async Task InvokeAsync_InHalfOpen_WhenNextCallFails_ImmediatelyTripsBackToOpenWithSingleFailure()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3, // Requires 3 failures normally
            BreakDuration = TimeSpan.FromSeconds(10),
            TimeProvider = timeProvider
        };
        var middleware = new CircuitBreakerMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("test.circuit", "corr-1");

        // 3 failures trip open
        await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err", "Fail"))), CancellationToken.None);
        await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err", "Fail"))), CancellationToken.None);
        await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err", "Fail"))), CancellationToken.None);

        // Advance past break duration -> HalfOpen
        timeProvider.Advance(TimeSpan.FromSeconds(11));

        // Trial call fails (only 1 failure in HalfOpen)
        var trialResult = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err", "HalfOpen fail"))), CancellationToken.None);
        trialResult.IsFailure.Should().BeTrue();

        // Circuit immediately trips back to OPEN (rejects subsequent call immediately)
        var rejectedResult = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);
        rejectedResult.IsFailure.Should().BeTrue();
        rejectedResult.Error.Code.Should().Be("Messaging.CircuitBreaker.Open");
    }

    [Fact]
    public async Task InvokeAsync_ExceptionInNext_RecordsFailureAndRethrows()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            BreakDuration = TimeSpan.FromSeconds(10),
            TimeProvider = timeProvider
        };
        var middleware = new CircuitBreakerMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("test.circuit", "corr-1");

        // Act & Assert exception rethrow
        Func<Task> act = async () => await middleware.InvokeAsync(context, (ctx, ct) => throw new InvalidOperationException("boom"), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Next call should be rejected because failure threshold (1) was breached
        var result = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);
        result.Error.Code.Should().Be("Messaging.CircuitBreaker.Open");
    }

    [Fact]
    public void AddCircuitBreaker_WithOptions_RegistersMiddlewareInServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddMessaging(options =>
        {
            options.AddCircuitBreaker(cb =>
            {
                cb.FailureThreshold = 3;
                cb.BreakDuration = TimeSpan.FromSeconds(15);
            });
        });

        using var provider = services.BuildServiceProvider();
        var middlewares = provider.GetServices<IMessageMiddleware>();
        middlewares.Should().Contain(m => m is CircuitBreakerMiddleware);
    }

    [Fact]
    public async Task InvokeAsync_NullContext_ThrowsArgumentNullException()
    {
        var middleware = new CircuitBreakerMiddleware();
        Func<Task> act = async () => await middleware.InvokeAsync(null!, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InvokeAsync_NullNext_ThrowsArgumentNullException()
    {
        var middleware = new CircuitBreakerMiddleware();
        var context = TestMessageContextFactory.CreateContext("test.circuit", "corr-1");
        Func<Task> act = async () => await middleware.InvokeAsync(context, null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InvokeAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var middleware = new CircuitBreakerMiddleware();
        var context = TestMessageContextFactory.CreateContext("test.circuit", "corr-1");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void CircuitBreakerOptions_Properties_GetAndSetCorrectly()
    {
        var tp = TimeProvider.System;
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 10,
            SamplingDuration = TimeSpan.FromMinutes(2),
            BreakDuration = TimeSpan.FromMinutes(1),
            TimeProvider = tp
        };

        options.FailureThreshold.Should().Be(10);
        options.SamplingDuration.Should().Be(TimeSpan.FromMinutes(2));
        options.BreakDuration.Should().Be(TimeSpan.FromMinutes(1));
        options.TimeProvider.Should().BeSameAs(tp);
    }

    [Fact]
    public async Task Constructor_DefaultNullOptions_UsesDefaultValues()
    {
        var middleware = new CircuitBreakerMiddleware((IOptions<CircuitBreakerOptions>?)null);
        var context = TestMessageContextFactory.CreateContext("test.circuit", "corr-1");

        var result = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    private sealed class LogEntry
    {
        public Microsoft.Extensions.Logging.LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
    }

    private sealed class TestLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry
            {
                Level = logLevel,
                Message = formatter(state, exception),
                Exception = exception
            });
        }
    }

    [Fact]
    public async Task InvokeAsync_Transitions_LogsExpectedInformationAndWarnings()
    {
        var logger = new TestLogger<CircuitBreakerMiddleware>();
        var timeProvider = new ManualTimeProvider();
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            BreakDuration = TimeSpan.FromSeconds(10),
            TimeProvider = timeProvider
        };
        var middleware = new CircuitBreakerMiddleware(options, logger);
        var context = TestMessageContextFactory.CreateContext("test.circuit", "corr-1");

        // Trip open
        await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err1", "Fail 1"))), CancellationToken.None);
        logger.Entries.Should().ContainSingle(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
            e.Message.Contains("Circuit breaker TRIPPED OPEN. Failure threshold breached (1 consecutive failures)."));

        logger.Entries.Clear();
        timeProvider.Advance(TimeSpan.FromSeconds(11));

        // HalfOpen trial
        await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);
        logger.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information &&
            e.Message.Contains("Circuit breaker transitioned from OPEN to HALF-OPEN."));
        logger.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information &&
            e.Message.Contains("Circuit breaker recovered: transitioned from HALF-OPEN to CLOSED."));
    }

    private sealed class TrackingSynchronizationContext : SynchronizationContext
    {
        public int PostCount;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref PostCount);
            ThreadPool.QueueUserWorkItem(_ => d(state));
        }
    }

    [Fact]
    public void InvokeAsync_ConfiguresAwaitFalse_DoesNotCaptureSynchronizationContext()
    {
        var syncContext = new TrackingSynchronizationContext();
        var prevContext = SynchronizationContext.Current;
        try
        {
            var tcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
            var context = TestMessageContextFactory.CreateContext("test.circuit", "corr-1");
            var middleware = new CircuitBreakerMiddleware();

            SynchronizationContext.SetSynchronizationContext(syncContext);

            var valueTask = middleware.InvokeAsync(
                context,
                (ctx, ct) => new ValueTask<Result>(tcs.Task),
                CancellationToken.None);

            SynchronizationContext.SetSynchronizationContext(prevContext);

#pragma warning disable xUnit1031
            Task.Run(() => tcs.SetResult(Result.Success())).Wait();
            var result = valueTask.GetAwaiter().GetResult();
#pragma warning restore xUnit1031

            result.IsSuccess.Should().BeTrue();
            syncContext.PostCount.Should().Be(0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevContext);
        }
    }
}
