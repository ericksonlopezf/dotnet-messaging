// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Tests.Dispatch;

using System.Collections.Generic;
using System.Text;
using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Dispatch;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

[Trait("Category", "Unit")]
public class MessageConsumerTests
{
    private sealed class DummyHandlerRegistration : HandlerRegistrationBase
    {
        public DummyHandlerRegistration(string typeName) : base(typeName) { }
        public override void Register(DefaultMessageDispatcher dispatcher) { }
    }

    [Fact]
    public void Constructor_NullRequiredDependencies_ThrowsArgumentNullException()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();

        Action act1 = () => new MessageConsumer(null!, dispatcher, scopeFactory);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("transport");

        Action act2 = () => new MessageConsumer(transport, null!, scopeFactory);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("dispatcher");

        Action act3 = () => new MessageConsumer(transport, dispatcher, null!);
        act3.Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");
    }

    [Fact]
    public async Task Constructor_WithRegistrationsAndSubscribedDestinations_MergesDistinctDestinations()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();

        var registrations = new IHandlerRegistration[]
        {
            new DummyHandlerRegistration("event.created.v1"),
            new DummyHandlerRegistration("event.updated.v1"),
            new DummyHandlerRegistration("event.created.v1") // duplicate
        };

        var initialDestinations = new[] { "event.created.v1", "manual.destination" };

        var consumer = new MessageConsumer(
            transport,
            dispatcher,
            scopeFactory,
            subscribedDestinations: initialDestinations,
            registrations: registrations);

        await consumer.StartAsync();

        await transport.Received(1).SubscribeAsync(
            "event.created.v1",
            Arg.Any<Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>>(),
            Arg.Any<TransportSubscriptionOptions>(),
            Arg.Any<CancellationToken>());

        await transport.Received(1).SubscribeAsync(
            "event.updated.v1",
            Arg.Any<Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>>(),
            Arg.Any<TransportSubscriptionOptions>(),
            Arg.Any<CancellationToken>());

        await transport.Received(1).SubscribeAsync(
            "manual.destination",
            Arg.Any<Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>>(),
            Arg.Any<TransportSubscriptionOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddDestination_Invalid_ThrowsArgumentException(string? invalidDest)
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();

        var consumer = new MessageConsumer(transport, dispatcher, scopeFactory);
        Action act = () => consumer.AddDestination(invalidDest!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task AddDestination_Valid_AddsDistinctDestination()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();

        var consumer = new MessageConsumer(transport, dispatcher, scopeFactory);
        consumer.AddDestination("custom.queue");
        consumer.AddDestination("custom.queue"); // Duplicate ignored

        await consumer.StartAsync();

        await transport.Received(1).SubscribeAsync(
            "custom.queue",
            Arg.Any<Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>>(),
            Arg.Any<TransportSubscriptionOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_SubscribesToAllDestinations_AndIsIdempotent()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();

        var consumer = new MessageConsumer(
            transport,
            dispatcher,
            scopeFactory,
            subscribedDestinations: new[] { "topic.a", "topic.b" });

        // Act - 1st start
        await consumer.StartAsync(CancellationToken.None);

        // Act - 2nd start (idempotent, does not re-subscribe)
        await consumer.StartAsync(CancellationToken.None);

        // Assert
        await transport.Received(1).SubscribeAsync(
            "topic.a",
            Arg.Any<Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>>(),
            Arg.Is<TransportSubscriptionOptions>(opt => opt.MaxConcurrency == Environment.ProcessorCount * 2 && opt.PrefetchCount == 20),
            Arg.Any<CancellationToken>());

        await transport.Received(1).SubscribeAsync(
            "topic.b",
            Arg.Any<Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>>(),
            Arg.Is<TransportSubscriptionOptions>(opt => opt.MaxConcurrency == Environment.ProcessorCount * 2 && opt.PrefetchCount == 20),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnRawMessageReceivedAsync_WhenDispatchSucceeds_ReturnsAck()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var sp = Substitute.For<IServiceProvider>();

        scope.ServiceProvider.Returns(sp);
        scopeFactory.CreateScope().Returns(scope);

        Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>? callback = null;
        await transport.SubscribeAsync(
            "topic.a",
            Arg.Do<Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>>(cb => callback = cb),
            Arg.Any<TransportSubscriptionOptions>(),
            Arg.Any<CancellationToken>());

        dispatcher.DispatchAsync(
            "order.created",
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            sp,
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        var consumer = new MessageConsumer(
            transport,
            dispatcher,
            scopeFactory,
            subscribedDestinations: new[] { "topic.a" });

        await consumer.StartAsync();
        callback.Should().NotBeNull();

        var payload = new ReadOnlyMemory<byte>(new byte[] { 1, 2 });
        var metadata = TransportMessageMetadata.Create("order.created");

        // Act
        var ackResult = await callback!(payload, metadata, CancellationToken.None);

        // Assert
        ackResult.Should().Be(TransportAckResult.Ack);
        await dispatcher.Received(1).DispatchAsync("order.created", payload, metadata, sp, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnRawMessageReceivedAsync_WhenDispatchFailsFunctionally_LogsWarningAndReturnsAck()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var sp = Substitute.For<IServiceProvider>();
        var logger = new TestLogger<MessageConsumer>();

        scope.ServiceProvider.Returns(sp);
        scopeFactory.CreateScope().Returns(scope);

        Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>? callback = null;
        await transport.SubscribeAsync(
            "topic.a",
            Arg.Do<Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>>(cb => callback = cb),
            Arg.Any<TransportSubscriptionOptions>(),
            Arg.Any<CancellationToken>());

        dispatcher.DispatchAsync(
            "order.created",
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            sp,
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Failure(Error.Validation("Order.Invalid", "Invalid total"))));

        var consumer = new MessageConsumer(
            transport,
            dispatcher,
            scopeFactory,
            subscribedDestinations: new[] { "topic.a" },
            logger: logger);

        await consumer.StartAsync();

        // Act
        var ackResult = await callback!(new byte[] { 1 }, TransportMessageMetadata.Create("order.created"), CancellationToken.None);

        // Assert (Functional failures are Acked so poison messages don't block the queue)
        ackResult.Should().Be(TransportAckResult.Ack);
        logger.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
            e.Message.Contains("Message processing returned failure: Invalid total"));
    }

    [Fact]
    public async Task OnRawMessageReceivedAsync_WhenDispatchThrowsException_LogsErrorAndReturnsNackRequeue()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var sp = Substitute.For<IServiceProvider>();
        var logger = new TestLogger<MessageConsumer>();

        scope.ServiceProvider.Returns(sp);
        scopeFactory.CreateScope().Returns(scope);

        Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>? callback = null;
        await transport.SubscribeAsync(
            "topic.a",
            Arg.Do<Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>>(cb => callback = cb),
            Arg.Any<TransportSubscriptionOptions>(),
            Arg.Any<CancellationToken>());

        dispatcher.DispatchAsync(
            "order.created",
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            sp,
            Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Fatal database down"));

        var consumer = new MessageConsumer(
            transport,
            dispatcher,
            scopeFactory,
            subscribedDestinations: new[] { "topic.a" },
            logger: logger);

        await consumer.StartAsync();

        // Act
        var ackResult = await callback!(new byte[] { 1 }, TransportMessageMetadata.Create("order.created"), CancellationToken.None);

        // Assert
        ackResult.Should().Be(TransportAckResult.NackRequeue);
        logger.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error &&
            e.Message.Contains("Unhandled exception during message dispatch for order.created") &&
            e.Exception != null && e.Exception.Message.Contains("Fatal database down"));
    }

    [Fact]
    public async Task OnRawMessageReceivedAsync_WhenStoppedReceiving_ReturnsNackRequeueWithoutDispatch()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();

        Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>? callback = null;
        await transport.SubscribeAsync(
            "topic.a",
            Arg.Do<Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>>(cb => callback = cb),
            Arg.Any<TransportSubscriptionOptions>(),
            Arg.Any<CancellationToken>());

        var consumer = new MessageConsumer(
            transport,
            dispatcher,
            scopeFactory,
            subscribedDestinations: new[] { "topic.a" });

        await consumer.StartAsync();

        // Act - Stop accepting new messages
        await consumer.StopReceivingAsync();
        var ackResult = await callback!(new byte[] { 1 }, TransportMessageMetadata.Create("order.created"), CancellationToken.None);

        // Assert
        ackResult.Should().Be(TransportAckResult.NackRequeue);
        await dispatcher.DidNotReceive().DispatchAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<IServiceProvider>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DrainInFlightMessagesAsync_And_DisposeAsync_CompleteGracefully_AndIsIdempotent()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();

        var consumer = new MessageConsumer(transport, dispatcher, scopeFactory);

        // Act
        await consumer.DrainInFlightMessagesAsync(CancellationToken.None);
        await consumer.DisposeAsync();

        // Assert - Idempotency on second async dispose
        Func<Task> secondDispose = async () => await consumer.DisposeAsync();
        await secondDispose.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DrainInFlightMessagesAsync_WithActiveInFlightMessage_AwaitsRelease()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var sp = Substitute.For<IServiceProvider>();
        var logger = new TestLogger<MessageConsumer>();

        scope.ServiceProvider.Returns(sp);
        scopeFactory.CreateScope().Returns(scope);

        Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>? callback = null;
        await transport.SubscribeAsync(
            "topic.drain",
            Arg.Do<Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>>(cb => callback = cb),
            Arg.Any<TransportSubscriptionOptions>(),
            Arg.Any<CancellationToken>());

        var messageStartedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unblockTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.DispatchAsync(
            "order.created",
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            sp,
            Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                messageStartedTcs.TrySetResult();
                return new ValueTask<Result>(unblockTcs.Task.ContinueWith(static _ => Result.Success(), TaskScheduler.Default));
            });

        var consumer = new MessageConsumer(
            transport,
            dispatcher,
            scopeFactory,
            subscribedDestinations: new[] { "topic.drain" },
            logger: logger);

        await consumer.StartAsync();

        // Start processing message in background
        var processingTask = Task.Run(async () => await callback!(new byte[] { 1 }, TransportMessageMetadata.Create("order.created"), CancellationToken.None));
        await messageStartedTcs.Task;

        // Start draining with a cancellation token
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var drainTask = Task.Run(async () => await consumer.DrainInFlightMessagesAsync(cts.Token));

        // Start a second concurrent drain to exercise _drainTcs ??=
        var secondDrainTask = Task.Run(async () => await consumer.DrainInFlightMessagesAsync(cts.Token));

        try
        {
            await Task.Delay(50);
            drainTask.IsCompleted.Should().BeFalse();
            secondDrainTask.IsCompleted.Should().BeFalse();
        }
        finally
        {
            unblockTcs.TrySetResult();
        }

        await processingTask;
        await drainTask;
        await secondDrainTask;

        drainTask.IsCompletedSuccessfully.Should().BeTrue();
        secondDrainTask.IsCompletedSuccessfully.Should().BeTrue();

        logger.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information &&
            e.Message.Contains("Draining in-flight messages..."));
        logger.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information &&
            e.Message.Contains("In-flight messages successfully drained."));
    }

    [Fact]
    public void Dispose_Synchronous_CleansUpResourcesAndIsIdempotent()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();

        var consumer = new MessageConsumer(transport, dispatcher, scopeFactory);

        consumer.Dispose();

        // Assert - Idempotency on second synchronous dispose
        Action secondDispose = () => consumer.Dispose();
        secondDispose.Should().NotThrow();
    }

    [Fact]
    public async Task DisposeAsync_WhenInvoked_CleansUpResourcesAndIsIdempotent()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();

        var consumer = new MessageConsumer(transport, dispatcher, scopeFactory);

        await consumer.DisposeAsync();

        // Assert - Idempotency on second async dispose
        Func<Task> secondDispose = async () => await consumer.DisposeAsync();
        await secondDispose.Should().NotThrowAsync();
    }

    private sealed class NonBaseHandlerRegistration : IHandlerRegistration
    {
        public string TypeName => "non.base.v1";
        public void Register(DefaultMessageDispatcher dispatcher) { }
    }

    [Fact]
    public void Constructor_WithNonBaseRegistrations_IgnoresThemForDestinations()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();

        var registrations = new IHandlerRegistration[]
        {
            new NonBaseHandlerRegistration()
        };

        var consumer = new MessageConsumer(
            transport,
            dispatcher,
            scopeFactory,
            registrations: registrations);

        consumer.Should().NotBeNull();
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
    public async Task StartAsync_WhenExecutedAcrossLifecycle_LogsAllExpectedEntries()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var logger = new TestLogger<MessageConsumer>();

        var consumer = new MessageConsumer(
            transport,
            dispatcher,
            scopeFactory,
            subscribedDestinations: new[] { "order.topic" },
            logger: logger);

        await consumer.StartAsync();
        await consumer.StopReceivingAsync();
        await consumer.DrainInFlightMessagesAsync();

        logger.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information &&
            e.Message.Contains("Consumer subscribed to destination: order.topic"));
        logger.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information &&
            e.Message.Contains("Consumer stopped accepting new messages."));
        logger.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information &&
            e.Message.Contains("In-flight messages successfully drained."));
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
    public void StartAsync_ConfiguresAwaitFalse_DoesNotCaptureSynchronizationContext()
    {
        var syncContext = new TrackingSynchronizationContext();
        var prevContext = SynchronizationContext.Current;
        try
        {
            var transportTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
            var transport = Substitute.For<IMessageTransport>();
            transport.SubscribeAsync(
                Arg.Any<string>(),
                Arg.Any<Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>>(),
                Arg.Any<TransportSubscriptionOptions>(),
                Arg.Any<CancellationToken>())
                .Returns(_ => new ValueTask<Result>(transportTcs.Task));

            var dispatcher = Substitute.For<IMessageDispatcher>();
            var scopeFactory = Substitute.For<IServiceScopeFactory>();

            var consumer = new MessageConsumer(
                transport,
                dispatcher,
                scopeFactory,
                subscribedDestinations: new[] { "topic.a" });

            SynchronizationContext.SetSynchronizationContext(syncContext);

            var startValueTask = consumer.StartAsync(CancellationToken.None);

            SynchronizationContext.SetSynchronizationContext(prevContext);

#pragma warning disable xUnit1031
            Task.Run(() => transportTcs.SetResult(Result.Success())).Wait();
            startValueTask.GetAwaiter().GetResult();
#pragma warning restore xUnit1031

            syncContext.PostCount.Should().Be(0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevContext);
        }
    }

    [Fact]
    public void OnRawMessageReceivedAsync_ConfiguresAwaitFalse_DoesNotCaptureSynchronizationContext()
    {
        var syncContext = new TrackingSynchronizationContext();
        var prevContext = SynchronizationContext.Current;
        try
        {
            var dispatchTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
            var transport = Substitute.For<IMessageTransport>();
            var dispatcher = Substitute.For<IMessageDispatcher>();
            var scopeFactory = Substitute.For<IServiceScopeFactory>();
            var scope = Substitute.For<IServiceScope>();
            var sp = Substitute.For<IServiceProvider>();

            scope.ServiceProvider.Returns(sp);
            scopeFactory.CreateScope().Returns(scope);

            Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>? callback = null;
            transport.SubscribeAsync(
                "topic.a",
                Arg.Do<Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>>(cb => callback = cb),
                Arg.Any<TransportSubscriptionOptions>(),
                Arg.Any<CancellationToken>())
                .Returns(new ValueTask<Result>(Result.Success()));

            dispatcher.DispatchAsync(
                "order.created",
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<TransportMessageMetadata>(),
                sp,
                Arg.Any<CancellationToken>())
                .Returns(_ => new ValueTask<Result>(dispatchTcs.Task));

            var consumer = new MessageConsumer(
                transport,
                dispatcher,
                scopeFactory,
                subscribedDestinations: new[] { "topic.a" });

#pragma warning disable xUnit1031
            consumer.StartAsync().GetAwaiter().GetResult();
#pragma warning restore xUnit1031

            var metadata = TransportMessageMetadata.Create("order.created");

            SynchronizationContext.SetSynchronizationContext(syncContext);

            var ackValueTask = callback!(new byte[] { 1 }, metadata, CancellationToken.None);

            SynchronizationContext.SetSynchronizationContext(prevContext);

#pragma warning disable xUnit1031
            Task.Run(() => dispatchTcs.SetResult(Result.Success())).Wait();
            var ack = ackValueTask.GetAwaiter().GetResult();
#pragma warning restore xUnit1031

            ack.Should().Be(TransportAckResult.Ack);
            syncContext.PostCount.Should().Be(0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevContext);
        }
    }

    [Fact]
    public void DrainInFlightMessagesAsync_ConfiguresAwaitFalse_DoesNotCaptureSynchronizationContext()
    {
        var syncContext = new TrackingSynchronizationContext();
        var prevContext = SynchronizationContext.Current;
        try
        {
            var dispatchTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
            var transport = Substitute.For<IMessageTransport>();
            var dispatcher = Substitute.For<IMessageDispatcher>();
            var scopeFactory = Substitute.For<IServiceScopeFactory>();
            var scope = Substitute.For<IServiceScope>();
            var sp = Substitute.For<IServiceProvider>();

            scope.ServiceProvider.Returns(sp);
            scopeFactory.CreateScope().Returns(scope);

            Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>? callback = null;
            transport.SubscribeAsync(
                "topic.drain",
                Arg.Do<Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>>(cb => callback = cb),
                Arg.Any<TransportSubscriptionOptions>(),
                Arg.Any<CancellationToken>())
                .Returns(new ValueTask<Result>(Result.Success()));

            dispatcher.DispatchAsync(
                "order.created",
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<TransportMessageMetadata>(),
                sp,
                Arg.Any<CancellationToken>())
                .Returns(_ => new ValueTask<Result>(dispatchTcs.Task));

            var consumer = new MessageConsumer(
                transport,
                dispatcher,
                scopeFactory,
                subscribedDestinations: new[] { "topic.drain" });

#pragma warning disable xUnit1031
            consumer.StartAsync().GetAwaiter().GetResult();
#pragma warning restore xUnit1031

            var metadata = TransportMessageMetadata.Create("order.created");
            var ackValueTask = callback!(new byte[] { 1 }, metadata, CancellationToken.None);

            SynchronizationContext.SetSynchronizationContext(syncContext);

            var drainValueTask = consumer.DrainInFlightMessagesAsync(CancellationToken.None);

            SynchronizationContext.SetSynchronizationContext(prevContext);

#pragma warning disable xUnit1031
            Task.Run(() => dispatchTcs.SetResult(Result.Success())).Wait();
            ackValueTask.GetAwaiter().GetResult();
            drainValueTask.GetAwaiter().GetResult();
#pragma warning restore xUnit1031

            syncContext.PostCount.Should().Be(0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevContext);
        }
    }

    [Fact]
    public void DisposeAsync_ConfiguresAwaitFalse_DoesNotCaptureSynchronizationContext()
    {
        var syncContext = new TrackingSynchronizationContext();
        var prevContext = SynchronizationContext.Current;
        try
        {
            var transport = Substitute.For<IMessageTransport>();
            var dispatcher = Substitute.For<IMessageDispatcher>();
            var scopeFactory = Substitute.For<IServiceScopeFactory>();

            var consumer = new MessageConsumer(transport, dispatcher, scopeFactory);

            SynchronizationContext.SetSynchronizationContext(syncContext);

            var disposeValueTask = consumer.DisposeAsync();

            SynchronizationContext.SetSynchronizationContext(prevContext);

#pragma warning disable xUnit1031
            disposeValueTask.GetAwaiter().GetResult();
#pragma warning restore xUnit1031

            syncContext.PostCount.Should().Be(0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevContext);
        }
    }

    [Fact]
    public async Task DrainInFlightMessagesAsync_WhenNoInFlightMessages_CompletesImmediatelyAndLogs()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var logger = new TestLogger<MessageConsumer>();

        var consumer = new MessageConsumer(transport, dispatcher, scopeFactory, logger: logger);

        await consumer.DrainInFlightMessagesAsync(CancellationToken.None);

        logger.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information &&
            e.Message.Contains("Draining in-flight messages..."));
        logger.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information &&
            e.Message.Contains("In-flight messages successfully drained."));
    }

    [Fact]
    public async Task OnRawMessageReceivedAsync_WhenMessageReceived_IncrementsMessagesReceivedDiagnosticsMetric()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var sp = Substitute.For<IServiceProvider>();

        scope.ServiceProvider.Returns(sp);
        scopeFactory.CreateScope().Returns(scope);

        Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>? callback = null;
        await transport.SubscribeAsync(
            "topic.metric",
            Arg.Do<Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>>(cb => callback = cb),
            Arg.Any<TransportSubscriptionOptions>(),
            Arg.Any<CancellationToken>());

        dispatcher.DispatchAsync(
            "order.metric",
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            sp,
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        var consumer = new MessageConsumer(
            transport,
            dispatcher,
            scopeFactory,
            subscribedDestinations: new[] { "topic.metric" });

        await consumer.StartAsync();

        using var meterListener = new System.Diagnostics.Metrics.MeterListener();
        long recordedValue = 0;
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "EricksonLopez.Messaging" && instrument.Name == "messaging.receive.messages")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "messaging.receive.messages")
            {
                recordedValue += measurement;
            }
        });
        meterListener.Start();

        var before = recordedValue;
        var ack = await callback!(new byte[] { 1 }, TransportMessageMetadata.Create("order.metric"), CancellationToken.None);

        ack.Should().Be(TransportAckResult.Ack);
        (recordedValue - before).Should().Be(1);
    }
}




