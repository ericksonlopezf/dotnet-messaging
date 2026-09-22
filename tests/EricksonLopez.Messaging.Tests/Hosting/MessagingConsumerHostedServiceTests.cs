// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Tests.Hosting;

using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

[Trait("Category", "Unit")]
public class MessagingConsumerHostedServiceTests
{
    [Fact]
    public void Constructor_NullConsumer_ThrowsArgumentNullException()
    {
        Action act = () => new MessagingConsumerHostedService(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("consumer");
    }

    [Fact]
    public async Task StartAsync_WhenStopped_ManagesConsumerLifecycleGracefully()
    {
        // Arrange
        var consumer = Substitute.For<IMessageConsumer>();
        var logger = Substitute.For<ILogger<MessagingConsumerHostedService>>();

        var service = new MessagingConsumerHostedService(consumer, logger);
        using var cts = new CancellationTokenSource();

        var startedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.StartAsync(Arg.Any<CancellationToken>()).Returns(_ => { startedTcs.TrySetResult(); return ValueTask.CompletedTask; });

        // Act - Start service
        await service.StartAsync(cts.Token);
        await startedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        // Assert consumer started
        await consumer.Received(1).StartAsync(Arg.Any<CancellationToken>());

        // Act - Stop service
        await service.StopAsync(CancellationToken.None);

        // Assert consumer stopped receiving and drained in flight
        await consumer.Received(1).StopReceivingAsync();
        await consumer.Received(1).DrainInFlightMessagesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancellationSignaled_ExitsGracefully()
    {
        // Arrange
        var consumer = Substitute.For<IMessageConsumer>();
        var startedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.StartAsync(Arg.Any<CancellationToken>()).Returns(_ => { startedTcs.TrySetResult(); return ValueTask.CompletedTask; });

        var service = new MessagingConsumerHostedService(consumer);
        using var cts = new CancellationTokenSource();

        // Act - Start service and wait for consumer to start, then cancel
        var startTask = service.StartAsync(cts.Token);
        await startedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cts.Cancel();
        await startTask;

        if (service.ExecuteTask is not null)
        {
            try
            {
                await service.ExecuteTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        // Assert
        await consumer.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    private sealed class LogEntry
    {
        public Microsoft.Extensions.Logging.LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
    }

    private sealed class TestLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public System.Collections.Generic.List<LogEntry> Entries { get; } = new();

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
    public async Task StartAsync_WithCustomLogger_LogsStartingAndStopping()
    {
        var consumer = Substitute.For<IMessageConsumer>();
        var startedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.StartAsync(Arg.Any<CancellationToken>()).Returns(_ => { startedTcs.TrySetResult(); return ValueTask.CompletedTask; });

        var logger = new TestLogger<MessagingConsumerHostedService>();
        var service = new MessagingConsumerHostedService(consumer, logger);
        using var cts = new CancellationTokenSource();

        var startTask = service.StartAsync(cts.Token);
        await startedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cts.Cancel();
        await startTask;

        logger.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information &&
            e.Message.Contains("Starting MessagingConsumerHostedService..."));

        await service.StopAsync(CancellationToken.None);

        logger.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information &&
            e.Message.Contains("Stopping MessagingConsumerHostedService..."));
    }

    [Fact]
    public async Task ExecuteAsync_WhenStarted_KeepsRunningUntilStoppingTokenCancelled()
    {
        var consumer = Substitute.For<IMessageConsumer>();
        var startedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.StartAsync(Arg.Any<CancellationToken>()).Returns(_ => { startedTcs.TrySetResult(); return ValueTask.CompletedTask; });

        var service = new MessagingConsumerHostedService(consumer);
        using var cts = new CancellationTokenSource();

        var startTask = service.StartAsync(cts.Token);
        await startedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        service.ExecuteTask.Should().NotBeNull();
        service.ExecuteTask!.IsCompleted.Should().BeFalse();

        cts.Cancel();
        try
        {
            await service.ExecuteTask!;
        }
        catch (OperationCanceledException)
        {
        }

        service.ExecuteTask.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_WhenInvoked_InvokesBaseStopAsyncAndCompletesExecution()
    {
        var consumer = Substitute.For<IMessageConsumer>();
        var service = new MessagingConsumerHostedService(consumer);

        await service.StartAsync(CancellationToken.None);

        service.ExecuteTask.Should().NotBeNull();
        service.ExecuteTask!.IsCompleted.Should().BeFalse();

        await service.StopAsync(CancellationToken.None);

        service.ExecuteTask!.IsCompleted.Should().BeTrue();
        await consumer.Received(1).StopReceivingAsync();
        await consumer.Received(1).DrainInFlightMessagesAsync(Arg.Any<CancellationToken>());
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
    public void ExecuteAsync_ConfiguresAwaitFalse_DoesNotCaptureSynchronizationContext()
    {
        var syncContext = new TrackingSynchronizationContext();
        var prevContext = SynchronizationContext.Current;
        try
        {
            var startTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var consumer = Substitute.For<IMessageConsumer>();
            consumer.StartAsync(Arg.Any<CancellationToken>()).Returns(_ => new ValueTask(startTcs.Task));

            var service = new MessagingConsumerHostedService(consumer);
            using var cts = new CancellationTokenSource();

            SynchronizationContext.SetSynchronizationContext(syncContext);

            var startTask = service.StartAsync(cts.Token);

            SynchronizationContext.SetSynchronizationContext(prevContext);

#pragma warning disable xUnit1031
            Task.Run(() => startTcs.SetResult()).Wait();
            cts.Cancel();
            startTask.GetAwaiter().GetResult();
#pragma warning restore xUnit1031

            syncContext.PostCount.Should().Be(0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevContext);
        }
    }

    [Fact]
    public void ExecuteAsync_StoppingTokenTcs_ConfiguresAwaitFalse_DoesNotCaptureSynchronizationContext()
    {
        var syncContext = new TrackingSynchronizationContext();
        var prevContext = SynchronizationContext.Current;
        try
        {
            var consumer = Substitute.For<IMessageConsumer>();
            consumer.StartAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.CompletedTask);

            var service = new MessagingConsumerHostedService(consumer);
            using var cts = new CancellationTokenSource();

            SynchronizationContext.SetSynchronizationContext(syncContext);

            var startTask = service.StartAsync(cts.Token);

            SynchronizationContext.SetSynchronizationContext(prevContext);

#pragma warning disable xUnit1031
            Task.Run(() => cts.Cancel()).Wait();
            startTask.GetAwaiter().GetResult();
#pragma warning restore xUnit1031

            syncContext.PostCount.Should().Be(0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevContext);
        }
    }

    [Fact]
    public void StopAsync_DrainInFlight_ConfiguresAwaitFalse_DoesNotCaptureSynchronizationContext()
    {
        var syncContext = new TrackingSynchronizationContext();
        var prevContext = SynchronizationContext.Current;
        try
        {
            var drainTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var consumer = Substitute.For<IMessageConsumer>();
            consumer.StopReceivingAsync().Returns(ValueTask.CompletedTask);
            consumer.DrainInFlightMessagesAsync(Arg.Any<CancellationToken>()).Returns(_ => new ValueTask(drainTcs.Task));

            var service = new MessagingConsumerHostedService(consumer);
            using var cts = new CancellationTokenSource();

            var startTask = service.StartAsync(cts.Token);
            cts.Cancel();

            SynchronizationContext.SetSynchronizationContext(syncContext);

            var stopTask = service.StopAsync(CancellationToken.None);

            SynchronizationContext.SetSynchronizationContext(prevContext);

#pragma warning disable xUnit1031
            Task.Run(() => drainTcs.SetResult()).Wait();
            stopTask.GetAwaiter().GetResult();
#pragma warning restore xUnit1031

            syncContext.PostCount.Should().Be(0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevContext);
        }
    }

    [Fact]
    public void StopAsync_BaseStopAsync_ConfiguresAwaitFalse_DoesNotCaptureSynchronizationContext()
    {
        var syncContext = new TrackingSynchronizationContext();
        var prevContext = SynchronizationContext.Current;
        try
        {
            var consumer = Substitute.For<IMessageConsumer>();
            consumer.StopReceivingAsync().Returns(ValueTask.CompletedTask);
            consumer.DrainInFlightMessagesAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.CompletedTask);

            var service = new MessagingConsumerHostedService(consumer);

            var startTask = service.StartAsync(CancellationToken.None);

            SynchronizationContext.SetSynchronizationContext(syncContext);

            var stopTask = service.StopAsync(CancellationToken.None);

            SynchronizationContext.SetSynchronizationContext(prevContext);

#pragma warning disable xUnit1031
            stopTask.GetAwaiter().GetResult();
#pragma warning restore xUnit1031

            syncContext.PostCount.Should().Be(0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevContext);
        }
    }

    [Fact]
    public void StopAsync_StopReceiving_ConfiguresAwaitFalse_DoesNotCaptureSynchronizationContext()
    {
        var syncContext = new TrackingSynchronizationContext();
        var prevContext = SynchronizationContext.Current;
        try
        {
            var stopReceivingTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var consumer = Substitute.For<IMessageConsumer>();
            consumer.StopReceivingAsync().Returns(_ => new ValueTask(stopReceivingTcs.Task));
            consumer.DrainInFlightMessagesAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.CompletedTask);

            var service = new MessagingConsumerHostedService(consumer);
            using var cts = new CancellationTokenSource();

            var startTask = service.StartAsync(cts.Token);
            cts.Cancel();

            SynchronizationContext.SetSynchronizationContext(syncContext);

            var stopTask = service.StopAsync(CancellationToken.None);

            SynchronizationContext.SetSynchronizationContext(prevContext);

#pragma warning disable xUnit1031
            Task.Run(() => stopReceivingTcs.SetResult()).Wait();
            stopTask.GetAwaiter().GetResult();
#pragma warning restore xUnit1031

            syncContext.PostCount.Should().Be(0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevContext);
        }
    }
}



