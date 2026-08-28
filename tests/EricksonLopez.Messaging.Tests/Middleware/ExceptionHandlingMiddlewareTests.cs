// Copyright © Erickson Lopez. MIT License.
using System;
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
public class ExceptionHandlingMiddlewareTests
{
    private readonly ExceptionHandlingMiddleware _middleware = new();

    [Fact]
    public async Task InvokeAsync_WhenNextSucceeds_ReturnsSuccessResult()
    {
        // Arrange
        var context = TestMessageContextFactory.CreateContext();

        // Act
        var result = await _middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Success()),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhenNextReturnsFailure_ReturnsSameFailure()
    {
        // Arrange
        var context = TestMessageContextFactory.CreateContext();
        var expectedError = Error.Validation("Custom.Validation", "Invalid payload");

        // Act
        var result = await _middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Failure(expectedError)),
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Custom.Validation");
        result.Error.Description.Should().Be("Invalid payload");
    }

    [Fact]
    public async Task InvokeAsync_WhenNextThrowsException_ReturnsUnexpectedFailureWithErrorDetails()
    {
        // Arrange
        var context = TestMessageContextFactory.CreateContext();
        var exception = new InvalidOperationException("Database connection timeout");

        // Act
        var result = await _middleware.InvokeAsync(
            context,
            (ctx, ct) => throw exception,
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Messaging.UnhandledException");
        result.Error.Description.Should().Be(
            $"Unhandled exception of type '{nameof(InvalidOperationException)}': Database connection timeout");
    }

    [Fact]
    public async Task InvokeAsync_WhenCancellationRequested_ReturnsCancelledFailure()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var context = TestMessageContextFactory.CreateContext(cts.Token);

        // Act
        var result = await _middleware.InvokeAsync(
            context,
            (ctx, ct) => throw new OperationCanceledException(cts.Token),
            cts.Token);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Messaging.Cancelled");
        result.Error.Description.Should().Be("Message processing was cancelled by caller or host shutdown.");
    }

    [Fact]
    public async Task InvokeAsync_WhenOperationCanceledExceptionThrownWithoutCancellationRequested_ReturnsUnhandledExceptionFailure()
    {
        // Arrange
        var context = TestMessageContextFactory.CreateContext(CancellationToken.None);

        // Act
        var result = await _middleware.InvokeAsync(
            context,
            (ctx, ct) => throw new OperationCanceledException("Internal task cancel"),
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Messaging.UnhandledException");
        result.Error.Description.Should().Be(
            $"Unhandled exception of type '{nameof(OperationCanceledException)}': Internal task cancel");
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
            var context = TestMessageContextFactory.CreateContext();

            // Set SynchronizationContext BEFORE invoking middleware
            SynchronizationContext.SetSynchronizationContext(syncContext);

            var valueTask = _middleware.InvokeAsync(
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



