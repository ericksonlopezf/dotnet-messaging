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
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
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
            e.Message.Contains("Circuit breaker TRIPPED OPEN.") &&
            e.Message.Contains("1 consecutive failures"));

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

    [Fact]
    public async Task InvokeAsync_FailureFilteredOut_DoesNotTripCircuit()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            BreakDuration = TimeSpan.FromSeconds(10),
            TimeProvider = timeProvider,
            FailureFilter = err => err.Type != ErrorType.Validation
        };
        var middleware = new CircuitBreakerMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("test.circuit", "corr-1");

        // 5 validation failures occur
        for (int i = 0; i < 5; i++)
        {
            var r = await middleware.InvokeAsync(
                context,
                (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Validation("Test.Validation", "Invalid format"))),
                CancellationToken.None);
            r.IsFailure.Should().BeTrue();
        }

        // Circuit should still be CLOSED, next call should execute and succeed
        var nextResult = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Success()),
            CancellationToken.None);

        nextResult.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_FailureMatchesFilter_TripsCircuit()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            BreakDuration = TimeSpan.FromSeconds(10),
            TimeProvider = timeProvider,
            FailureFilter = err => err.Type != ErrorType.Validation
        };
        var middleware = new CircuitBreakerMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("test.circuit", "corr-1");

        // 2 infrastructure failures occur (matching filter)
        for (int i = 0; i < 2; i++)
        {
            var r = await middleware.InvokeAsync(
                context,
                (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Database.Down", "Connection refused"))),
                CancellationToken.None);
            r.IsFailure.Should().BeTrue();
        }

        // Circuit should now be OPEN, next call rejected immediately with CircuitBreaker.Open error
        var rejectedResult = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Success()),
            CancellationToken.None);

        rejectedResult.IsFailure.Should().BeTrue();
        rejectedResult.Error.Code.Should().Be("Messaging.CircuitBreaker.Open");
    }

    [Fact]
    public async Task InvokeAsync_SamplingDurationExpires_ResetsFailureCounter()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            SamplingDuration = TimeSpan.FromSeconds(10),
            BreakDuration = TimeSpan.FromSeconds(30),
            TimeProvider = timeProvider
        };
        var middleware = new CircuitBreakerMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("test.circuit", "corr-1");

        // Failure 1 at t=0
        var res1 = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err1", "Err1"))),
            CancellationToken.None);
        res1.IsFailure.Should().BeTrue();

        // Advance past SamplingDuration (10s -> 11s)
        timeProvider.Advance(TimeSpan.FromSeconds(11));

        // Failure 2 at t=11s. Because window expired, counter was reset to 0 and becomes 1. Circuit must remain CLOSED.
        var res2 = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err2", "Err2"))),
            CancellationToken.None);
        res2.IsFailure.Should().BeTrue();

        // Next call succeeds because circuit is still closed
        var res3 = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Success()),
            CancellationToken.None);
        res3.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_SuccessAfterFailureInClosedState_ResetsConsecutiveFailures()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            SamplingDuration = TimeSpan.FromSeconds(60),
            BreakDuration = TimeSpan.FromSeconds(30),
            TimeProvider = timeProvider
        };
        var middleware = new CircuitBreakerMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("test.circuit", "corr-1");

        // Failure 1 (consecutive failures = 1)
        await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err1", "Err1"))),
            CancellationToken.None);

        // Success resets consecutive failures to 0
        var successRes = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Success()),
            CancellationToken.None);
        successRes.IsSuccess.Should().BeTrue();

        // Another failure (consecutive failures = 1, not 2)
        var failureRes = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err2", "Err2"))),
            CancellationToken.None);
        failureRes.IsFailure.Should().BeTrue();

        // Next call should still execute because circuit is still CLOSED
        var nextCall = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Success()),
            CancellationToken.None);
        nextCall.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void RecordSuccess_WhenHalfOpenAndZeroConsecutiveFailures_TransitionsToClosed()
    {
        var middleware = new CircuitBreakerMiddleware(new CircuitBreakerOptions());
        var stateField = typeof(CircuitBreakerMiddleware).GetField("_state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var failuresField = typeof(CircuitBreakerMiddleware).GetField("_consecutiveFailures", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var halfOpenVal = Enum.Parse(stateField!.FieldType, "HalfOpen");
        var closedVal = Enum.Parse(stateField!.FieldType, "Closed");

        stateField.SetValue(middleware, halfOpenVal);
        failuresField!.SetValue(middleware, 0);

        var recordSuccess = typeof(CircuitBreakerMiddleware).GetMethod("RecordSuccess", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        recordSuccess!.Invoke(middleware, null);

        stateField.GetValue(middleware).Should().Be(closedVal);
    }

    [Fact]
    public void RecordSuccess_WhenClosedAndZeroFailures_DoesNotAcquireLock()
    {
        var middleware = new CircuitBreakerMiddleware(new CircuitBreakerOptions());
        var lockField = typeof(CircuitBreakerMiddleware).GetField("_lock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var lockObj = lockField!.GetValue(middleware)!;

        var lockAcquired = new ManualResetEventSlim(false);
        var releaseLock = new ManualResetEventSlim(false);

        var bgTask = Task.Run(() =>
        {
            lock (lockObj)
            {
                lockAcquired.Set();
                releaseLock.Wait();
            }
        });

        lockAcquired.Wait(5000).Should().BeTrue();
        try
        {
            var recordSuccess = typeof(CircuitBreakerMiddleware).GetMethod("RecordSuccess", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var completed = Task.Factory.StartNew(() => recordSuccess!.Invoke(middleware, null), TaskCreationOptions.LongRunning).Wait(5000);
            completed.Should().BeTrue("RecordSuccess must fast-path return without acquiring the lock when already closed with 0 failures");
        }
        finally
        {
            releaseLock.Set();
            bgTask.Wait(5000);
        }
    }

    [Fact]
    public async Task RecordFailure_ExactSamplingDurationBoundary_ResetsFailureCounter()
    {
        var timeProvider = new ManualTimeProvider();
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            SamplingDuration = TimeSpan.FromSeconds(60),
            BreakDuration = TimeSpan.FromSeconds(30),
            TimeProvider = timeProvider
        };
        var middleware = new CircuitBreakerMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("test.circuit", "corr-exact");

        // Failure 1 at t=0
        await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err1", "Err1"))), CancellationToken.None);

        // Advance by EXACTLY SamplingDuration (60s)
        timeProvider.Advance(options.SamplingDuration);

        // Failure 2 at t=60s (should reset counter to 0 first, then become 1 -> NOT trip)
        var failResult = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Failure(Error.Failure("Err2", "Err2"))), CancellationToken.None);
        failResult.IsFailure.Should().BeTrue();

        // Next call should still execute because circuit remained CLOSED
        var nextCall = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);
        nextCall.IsSuccess.Should().BeTrue();
    }
}


