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
using Xunit;
using Result = EricksonLopez.Result.Result;

[Trait("Category", "Unit")]
public class HandlerTimeoutMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_HandlerCompletesWithinTimeout_ReturnsSuccess()
    {
        var options = new HandlerTimeoutOptions
        {
            Timeout = TimeSpan.FromMilliseconds(50)
        };
        var middleware = new HandlerTimeoutMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("test.timeout", "corr-1");

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Success()),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_HandlerExceedsTimeout_ReturnsTimeoutFailure()
    {
        var options = new HandlerTimeoutOptions
        {
            Timeout = TimeSpan.FromMilliseconds(5)
        };
        var middleware = new HandlerTimeoutMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("test.timeout", "corr-1");

        // Act
        var result = await middleware.InvokeAsync(
            context,
            async (ctx, ct) =>
            {
                var tcs = new TaskCompletionSource<Result>();
                using (ct.Register(() => tcs.TrySetCanceled(ct)))
                {
                    return await tcs.Task;
                }
            },
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Messaging.Handler.Timeout");
        result.Error.Description.Should().Be($"Message processing exceeded the configured timeout of {options.Timeout.TotalSeconds}s.");
    }

    [Fact]
    public async Task InvokeAsync_InnerOperationCanceledExceptionWithoutTimeout_RethrowsOperationCanceledException()
    {
        var options = new HandlerTimeoutOptions
        {
            Timeout = TimeSpan.FromMilliseconds(50)
        };
        var middleware = new HandlerTimeoutMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("test.timeout", "corr-1");
        using var innerCts = new CancellationTokenSource();
        innerCts.Cancel();

        Func<Task> act = async () => await middleware.InvokeAsync(
            context,
            (ctx, ct) => throw new OperationCanceledException(innerCts.Token),
            CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task InvokeAsync_UserCancellationDuringExecution_RethrowsOperationCanceledException()
    {
        var options = new HandlerTimeoutOptions
        {
            Timeout = TimeSpan.FromMilliseconds(50)
        };
        var middleware = new HandlerTimeoutMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("test.timeout", "corr-1");
        using var userCts = new CancellationTokenSource();

        Func<Task> act = async () => await middleware.InvokeAsync(
            context,
            (ctx, ct) =>
            {
                userCts.Cancel();
                throw new OperationCanceledException(userCts.Token);
            },
            userCts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1000)]
    public async Task InvokeAsync_ZeroOrInfiniteOrNegativeTimeout_DoesNotEnforceTimeout(int timeoutMs)
    {
        var options = new HandlerTimeoutOptions
        {
            Timeout = timeoutMs == -1 ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(timeoutMs)
        };
        var middleware = new HandlerTimeoutMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("test.timeout", "corr-1");

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return ValueTask.FromResult(Result.Success());
            },
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_UserCancellationRequested_RethrowsOperationCanceledException()
    {
        var options = new HandlerTimeoutOptions
        {
            Timeout = TimeSpan.FromMilliseconds(50)
        };
        var middleware = new HandlerTimeoutMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("test.timeout", "corr-1");
        using var userCts = new CancellationTokenSource();
        userCts.Cancel();

        // Act & Assert
        Func<Task> act = async () => await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Success()),
            userCts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void AddHandlerTimeout_WithTimeout_RegistersMiddlewareInServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddMessaging(options =>
        {
            options.AddHandlerTimeout(TimeSpan.FromSeconds(10));
        });

        using var provider = services.BuildServiceProvider();
        var middlewares = provider.GetServices<IMessageMiddleware>();
        middlewares.Should().Contain(m => m is HandlerTimeoutMiddleware);
    }

    [Fact]
    public async Task InvokeAsync_NullContext_ThrowsArgumentNullException()
    {
        var middleware = new HandlerTimeoutMiddleware();
        Func<Task> act = async () => await middleware.InvokeAsync(null!, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InvokeAsync_NullNext_ThrowsArgumentNullException()
    {
        var middleware = new HandlerTimeoutMiddleware();
        var context = TestMessageContextFactory.CreateContext("test.timeout", "corr-1");
        Func<Task> act = async () => await middleware.InvokeAsync(context, null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InvokeAsync_PreCancelledToken_ThrowsOperationCanceledException()
    {
        var middleware = new HandlerTimeoutMiddleware();
        var context = TestMessageContextFactory.CreateContext("test.timeout", "corr-1");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void HandlerTimeoutOptions_Properties_GetAndSetCorrectly()
    {
        var tp = TimeProvider.System;
        var options = new HandlerTimeoutOptions
        {
            Timeout = TimeSpan.FromMinutes(5),
            TimeProvider = tp
        };

        options.Timeout.Should().Be(TimeSpan.FromMinutes(5));
        options.TimeProvider.Should().BeSameAs(tp);
    }

    [Fact]
    public async Task Constructor_DefaultNullOptions_UsesDefaultValues()
    {
        var middleware = new HandlerTimeoutMiddleware((Microsoft.Extensions.Options.IOptions<HandlerTimeoutOptions>?)null);
        var context = TestMessageContextFactory.CreateContext("test.timeout", "corr-1");

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
    public async Task InvokeAsync_WhenTimeoutOccurs_LogsWarningWithDetails()
    {
        var logger = new TestLogger<HandlerTimeoutMiddleware>();
        var options = new HandlerTimeoutOptions
        {
            Timeout = TimeSpan.FromMilliseconds(5)
        };
        var middleware = new HandlerTimeoutMiddleware(options, logger);
        var context = TestMessageContextFactory.CreateContext("test.timeout", "corr-1");

        var result = await middleware.InvokeAsync(
            context,
            async (ctx, ct) =>
            {
                var tcs = new TaskCompletionSource<Result>();
                using (ct.Register(() => tcs.TrySetCanceled(ct)))
                {
                    return await tcs.Task;
                }
            },
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        logger.Entries.Should().ContainSingle(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
            e.Message.Contains("Message processing timed out after") &&
            e.Message.Contains(context.Metadata.MessageId));
    }

    private sealed class TrackingTimeProvider : TimeProvider
    {
        public bool TimerCreated;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            TimerCreated = true;
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
    public async Task Constructor_CustomTimeProvider_UsesProvidedTimeProvider()
    {
        var tp = new TrackingTimeProvider();
        var options = new HandlerTimeoutOptions
        {
            Timeout = TimeSpan.FromMilliseconds(50),
            TimeProvider = tp
        };
        var middleware = new HandlerTimeoutMiddleware(options);
        var context = TestMessageContextFactory.CreateContext("test.timeout", "corr-1");

        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Success()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tp.TimerCreated.Should().BeTrue();
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
            var context = TestMessageContextFactory.CreateContext("test.timeout", "corr-1");
            var middleware = new HandlerTimeoutMiddleware(new HandlerTimeoutOptions { Timeout = TimeSpan.Zero });

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
    public void InvokeAsync_WithTimeoutConfiguresAwaitFalse_DoesNotCaptureSynchronizationContext()
    {
        var syncContext = new TrackingSynchronizationContext();
        var prevContext = SynchronizationContext.Current;
        try
        {
            var tcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
            var context = TestMessageContextFactory.CreateContext("test.timeout", "corr-1");
            var middleware = new HandlerTimeoutMiddleware(new HandlerTimeoutOptions { Timeout = TimeSpan.FromMilliseconds(50) });

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
