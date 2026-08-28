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
using Microsoft.Extensions.Logging;
using Xunit;

[Trait("Category", "Unit")]
public class LoggingMiddlewareTests
{
    private sealed class LogEntry
    {
        public LogLevel Level { get; set; }
        public EventId EventId { get; set; }
        public string Message { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();
        public LogLevel MinLogLevel { get; set; } = LogLevel.Trace;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= MinLogLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            Entries.Add(new LogEntry
            {
                Level = logLevel,
                EventId = eventId,
                Message = formatter(state, exception),
                Exception = exception
            });
        }
    }

    [Fact]
    public async Task Constructor_DefaultNullLogger_RunsWithoutExceptions()
    {
        // Arrange
        var middleware = new LoggingMiddleware(null);
        var context = TestMessageContextFactory.CreateContext("orders.created.v1", "corr-555");

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Success()),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhenSuccessAndInformationEnabled_LogsStartAndSuccess()
    {
        // Arrange
        var logger = new TestLogger<LoggingMiddleware> { MinLogLevel = LogLevel.Information };
        var middleware = new LoggingMiddleware(logger);
        var context = TestMessageContextFactory.CreateContext("orders.created.v1", "corr-555");

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Success()),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        logger.Entries.Should().HaveCount(2);

        logger.Entries[0].Level.Should().Be(LogLevel.Information);
        logger.Entries[0].Message.Should().Contain("Processing message orders.created.v1");
        logger.Entries[0].Message.Should().Contain(context.Metadata.MessageId);
        logger.Entries[0].Message.Should().Contain("corr-555");

        logger.Entries[1].Level.Should().Be(LogLevel.Information);
        logger.Entries[1].Message.Should().Contain("Successfully processed message orders.created.v1");
        logger.Entries[1].Message.Should().Contain(context.Metadata.MessageId);
        logger.Entries[1].Message.Should().Contain("ms");
    }

    [Fact]
    public async Task InvokeAsync_WhenSuccessAndInformationDisabled_DoesNotLogStartOrSuccess()
    {
        // Arrange
        var logger = new TestLogger<LoggingMiddleware> { MinLogLevel = LogLevel.Warning };
        var middleware = new LoggingMiddleware(logger);
        var context = TestMessageContextFactory.CreateContext("orders.created.v1", "corr-555");

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Success()),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_WhenBusinessFailure_LogsWarningWithErrorCodeAndDescription()
    {
        // Arrange
        var logger = new TestLogger<LoggingMiddleware> { MinLogLevel = LogLevel.Information };
        var middleware = new LoggingMiddleware(logger);
        var context = TestMessageContextFactory.CreateContext("orders.created.v1", "corr-555");
        var failureError = Error.Validation("Order.InvalidAmount", "Amount must be positive");

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Failure(failureError)),
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        logger.Entries.Should().HaveCount(2);

        logger.Entries[0].Level.Should().Be(LogLevel.Information);
        logger.Entries[0].Message.Should().Contain("Processing message orders.created.v1");

        logger.Entries[1].Level.Should().Be(LogLevel.Warning);
        logger.Entries[1].Message.Should().Contain("Business failure processing message orders.created.v1");
        logger.Entries[1].Message.Should().Contain(context.Metadata.MessageId);
        logger.Entries[1].Message.Should().Contain("Order.InvalidAmount");
        logger.Entries[1].Message.Should().Contain("Amount must be positive");
    }

    [Fact]
    public async Task InvokeAsync_WhenExceptionThrown_LogsErrorAndRethrows()
    {
        // Arrange
        var logger = new TestLogger<LoggingMiddleware> { MinLogLevel = LogLevel.Information };
        var middleware = new LoggingMiddleware(logger);
        var context = TestMessageContextFactory.CreateContext("orders.created.v1", "corr-555");
        var expectedException = new InvalidOperationException("Fatal processor failure");

        // Act
        Func<Task> act = async () => await middleware.InvokeAsync(
            context,
            (ctx, ct) => throw expectedException,
            CancellationToken.None);

        // Assert
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(expectedException);

        logger.Entries.Should().HaveCount(2);
        logger.Entries[0].Level.Should().Be(LogLevel.Information);
        logger.Entries[0].Message.Should().Contain("Processing message orders.created.v1");

        logger.Entries[1].Level.Should().Be(LogLevel.Error);
        logger.Entries[1].Exception.Should().BeSameAs(expectedException);
        logger.Entries[1].Message.Should().Contain("Unhandled exception processing message orders.created.v1");
        logger.Entries[1].Message.Should().Contain(context.Metadata.MessageId);
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
        // Arrange
        var syncContext = new TrackingSynchronizationContext();
        var prevContext = SynchronizationContext.Current;
        try
        {
            var tcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
            var context = TestMessageContextFactory.CreateContext("orders.created.v1", "corr-555");
            var middleware = new LoggingMiddleware();

            // Set SynchronizationContext BEFORE invoking middleware
            SynchronizationContext.SetSynchronizationContext(syncContext);

            var valueTask = middleware.InvokeAsync(
                context,
                (ctx, ct) => new ValueTask<Result>(tcs.Task),
                CancellationToken.None);

            // Reset SynchronizationContext immediately after invocation setup
            SynchronizationContext.SetSynchronizationContext(prevContext);

            // Act - complete task on ThreadPool
#pragma warning disable xUnit1031
            Task.Run(() => tcs.SetResult(Result.Success())).Wait();

            var result = valueTask.GetAwaiter().GetResult();
#pragma warning restore xUnit1031

            // Assert
            result.IsSuccess.Should().BeTrue();
            syncContext.PostCount.Should().Be(0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevContext);
        }
    }
}



