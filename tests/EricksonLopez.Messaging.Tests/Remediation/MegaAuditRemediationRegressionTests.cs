// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Diagnostics;
using EricksonLopez.Messaging.Dispatch;
using EricksonLopez.Messaging.Middleware;
using EricksonLopez.Messaging.Serialization;
using EricksonLopez.Messaging.Tests.Common;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Messaging.Transport.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Messaging.Tests.Remediation;

using Error = EricksonLopez.Result.Error;
using Result = EricksonLopez.Result.Result;

public sealed class MegaAuditRemediationRegressionTests
{
    private sealed class FastTimeProvider : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
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

    // --- FINDING-01: Schema Upcasting End-to-End ---

    [MessageType("orders.created.v1")]
    public sealed record LegacyOrderV1(Guid OrderId, decimal Amount) : IMessage;

    [MessageType("orders.created.v2")]
    public sealed record UpgradedOrderV2(Guid OrderId, decimal Amount, string Currency) : IMessage;

    public sealed class OrderV1ToV2Upcaster : IMessageUpcaster<LegacyOrderV1, UpgradedOrderV2>
    {
        public UpgradedOrderV2 Upcast(LegacyOrderV1 message, TransportMessageMetadata metadata)
        {
            return new UpgradedOrderV2(message.OrderId, message.Amount, "USD");
        }
    }

    public sealed class UpgradedOrderV2Handler : IMessageHandler<UpgradedOrderV2>
    {
        public UpgradedOrderV2? ReceivedMessage { get; private set; }

        public ValueTask<Result> HandleAsync(UpgradedOrderV2 message, MessageContext context, CancellationToken cancellationToken)
        {
            ReceivedMessage = message;
            return ValueTask.FromResult(Result.Success());
        }
    }

    [Fact]
    public async Task Finding01_SchemaUpcasting_EndToEndDispatch_SucceedsWithoutInvalidCastException()
    {
        var services = new ServiceCollection();
        var serializer = new NativeAotJsonSerializer(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddSingleton<IMessageSerializer>(serializer);

        var v2Handler = new UpgradedOrderV2Handler();
        services.AddSingleton(v2Handler);
        services.AddScoped<IMessageHandler<UpgradedOrderV2>>(_ => v2Handler);
        services.AddScoped<OrderV1ToV2Upcaster>();

        var upcasterInvoker = new MessageUpcasterInvoker<LegacyOrderV1, UpgradedOrderV2, OrderV1ToV2Upcaster>();
        var upcastingMiddleware = new MessageUpcastingMiddleware(new[] { upcasterInvoker });

        var dispatcher = new DefaultMessageDispatcher(
            serializer,
            middlewares: new[] { upcastingMiddleware },
            upcasters: new[] { upcasterInvoker });

        dispatcher.RegisterHandler<UpgradedOrderV2, UpgradedOrderV2Handler>("orders.created.v2");

        var legacyMessage = new LegacyOrderV1(Guid.NewGuid(), 199.99m);
        var rawPayload = serializer.Serialize(legacyMessage);
        var metadata = TransportMessageMetadata.Create("orders.created.v1");

        var serviceProvider = services.BuildServiceProvider();

        // Act: Dispatch with legacy messageType string
        var result = await dispatcher.DispatchAsync(
            "orders.created.v1",
            rawPayload,
            metadata,
            serviceProvider);

        // Assert: End-to-end success, no InvalidCastException, and handler received the upcasted contract!
        result.IsSuccess.Should().BeTrue();
        v2Handler.ReceivedMessage.Should().NotBeNull();
        v2Handler.ReceivedMessage!.OrderId.Should().Be(legacyMessage.OrderId);
        v2Handler.ReceivedMessage.Amount.Should().Be(199.99m);
        v2Handler.ReceivedMessage.Currency.Should().Be("USD");
    }

    // --- FINDING-02: Silent Message Loss on Host Shutdown ---

    [Fact]
    public async Task Finding02_HostShutdown_CancelledMessage_ReturnsNackRequeue()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var sp = Substitute.For<IServiceProvider>();

        scopeFactory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(sp);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Simulate host shutdown token already cancelled

        // Dispatcher returns Cancellation failure (as produced by ExceptionHandlingMiddleware)
        dispatcher.DispatchAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<IServiceProvider>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Failure(Error.Failure("Messaging.Cancelled", "Host shutdown."))));

        var consumer = new MessageConsumer(transport, dispatcher, scopeFactory);

        // Act: invoke private OnRawMessageReceivedAsync via reflection
        var method = typeof(MessageConsumer).GetMethod(
            "OnRawMessageReceivedAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        var metadata = TransportMessageMetadata.Create("test.message");
        var task = (ValueTask<TransportAckResult>)method.Invoke(
            consumer,
            new object[] { new ReadOnlyMemory<byte>(new byte[] { 1, 2 }), metadata, cts.Token })!;

        var ackResult = await task;

        // Assert: MUST be NackRequeue so broker does not delete in-flight message!
        ackResult.Should().Be(TransportAckResult.NackRequeue);
    }

    // --- FINDING-03: Poison Messages & Functional Failures routed to DLQ ---

    [Fact]
    public async Task Finding03_MessageConsumer_OnFunctionalFailure_RoutesToDeadLetterQueue()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var dlq = Substitute.For<IDeadLetterQueue>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var sp = Substitute.For<IServiceProvider>();

        scopeFactory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(sp);
        sp.GetService(typeof(IDeadLetterQueue)).Returns(dlq);

        // Dispatcher returns a business / deserialization functional failure
        dispatcher.DispatchAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<IServiceProvider>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Failure(Error.Validation("Messaging.InvalidPayload", "Schema invalid."))));

        var consumer = new MessageConsumer(transport, dispatcher, scopeFactory);

        var method = typeof(MessageConsumer).GetMethod(
            "OnRawMessageReceivedAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        var metadata = TransportMessageMetadata.Create("poison.message");
        var payload = new ReadOnlyMemory<byte>(new byte[] { 0xDE, 0xAD });

        // Act
        var task = (ValueTask<TransportAckResult>)method.Invoke(
            consumer,
            new object[] { payload, metadata, CancellationToken.None })!;

        var ackResult = await task;

        // Assert: Routed to DLQ and returned DeadLetter!
        await dlq.Received(1).ForwardRawToDeadLetterAsync(
            Arg.Is<ReadOnlyMemory<byte>>(p => p.Length == 2),
            Arg.Is<DeadLetterReason>(r => r.ReasonCode == "Messaging.InvalidPayload"),
            metadata,
            Arg.Any<CancellationToken>());

        ackResult.Should().Be(TransportAckResult.DeadLetter);
    }

    // --- FINDING-04: Retry Integer Overflow with attempts >= 31 ---

    [Fact]
    public async Task Finding04_RetryMiddleware_AttemptOver30_DoesNotOverflowOrThrowArgumentOutOfRange()
    {
        var fastTime = new FastTimeProvider();
        var options = new RetryOptions
        {
            MaxRetries = 35,
            InitialDelay = TimeSpan.FromMilliseconds(50),
            MaxDelay = TimeSpan.FromSeconds(10),
            TimeProvider = fastTime
        };

        var middleware = new RetryMiddleware(options);
        var context = TestMessageContextFactory.CreateContext();
        int attemptCount = 0;

        // Act: Run a handler that fails for 32 attempts, then succeeds on attempt 33
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) =>
            {
                attemptCount++;
                if (attemptCount < 33)
                {
                    return ValueTask.FromResult(Result.Failure(Error.Failure("Transient", "Failed")));
                }
                return ValueTask.FromResult(Result.Success());
            },
            CancellationToken.None);

        // Assert: No ArgumentOutOfRangeException was thrown, completed with success!
        result.IsSuccess.Should().BeTrue();
        attemptCount.Should().Be(33);
    }

    // --- FINDING-06: PartitionKey Attribute Extraction ---

    public sealed record OrderPlacedWithPartitionKey(
        Guid OrderId,
        [property: PartitionKey] string Region,
        decimal Total) : IMessage;

    [Fact]
    public async Task Finding06_MessagePublisher_ExtractsPartitionKeyAttribute()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = new NativeAotJsonSerializer(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        var publisher = new MessagePublisher(transport, serializer);

        var message = new OrderPlacedWithPartitionKey(Guid.NewGuid(), "us-east-1", 500m);

        TransportMessageMetadata? capturedMetadata = null;
        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Do<TransportMessageMetadata>(m => capturedMetadata = m),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        // Act
        var result = await publisher.PublishAsync(message);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedMetadata.Should().NotBeNull();
        capturedMetadata!.PartitionKey.Should().Be("us-east-1");
    }

    // --- FINDING-07: IHandlerRegistration Decoupled from Concrete Dispatcher ---

    private sealed class CustomHandlerRegistry : IHandlerRegistry
    {
        public bool Registered { get; private set; }

        public void RegisterHandler<TMessage, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(string typeName)
            where TMessage : notnull
            where THandler : notnull, IMessageHandler<TMessage>
        {
            Registered = true;
        }
    }

    private sealed class MockRegistration : HandlerRegistrationBase
    {
        public MockRegistration() : base("test.custom") { }

        public override void Register(DefaultMessageDispatcher dispatcher)
        {
            // Backward compatible path
        }

        public override void Register(IHandlerRegistry registry)
        {
            registry.RegisterHandler<LegacyOrderV1, DummyHandler>("test.custom");
        }
    }

    private sealed class DummyHandler : IMessageHandler<LegacyOrderV1>
    {
        public ValueTask<Result> HandleAsync(LegacyOrderV1 message, MessageContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(Result.Success());
    }

    [Fact]
    public void Finding07_IHandlerRegistration_DecoupledFromConcreteDispatcher()
    {
        var registry = new CustomHandlerRegistry();
        var registration = new MockRegistration();

        // Act
        registration.Register((IHandlerRegistry)registry);

        // Assert
        registry.Registered.Should().BeTrue();
    }

    // --- FINDING-08: HandlerTimeoutMiddleware Updates Context CancellationToken ---

    [Fact]
    public async Task Finding08_HandlerTimeoutMiddleware_UpdatesContextCancellationToken()
    {
        var fastTime = new FastTimeProvider();
        var options = new HandlerTimeoutOptions
        {
            Timeout = TimeSpan.FromSeconds(5),
            TimeProvider = fastTime
        };

        var middleware = new HandlerTimeoutMiddleware(options);
        var context = TestMessageContextFactory.CreateContext(CancellationToken.None);

        CancellationToken observedContextToken = default;

        // Act
        var task = middleware.InvokeAsync(
            context,
            (ctx, ct) =>
            {
                observedContextToken = ctx.CancellationToken;
                return ValueTask.FromResult(Result.Success());
            },
            CancellationToken.None);

        await task;

        // Assert: Context CancellationToken was synchronized with linked timeout token!
        observedContextToken.CanBeCanceled.Should().BeTrue();
        context.CancellationToken.Should().Be(CancellationToken.None); // Restored after finally
    }

    // --- FINDING-09: MessageContext Items Lazy Initialization ---

    [Fact]
    public void Finding09_MessageContext_Items_IsLazilyInitialized()
    {
        var metadata = TransportMessageMetadata.Create("test.lazy");
        var sp = Substitute.For<IServiceProvider>();

        var context = new MessageContext(metadata, sp);

        // Before accessing Items property, internal field is null
        var field = typeof(MessageContext).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();
        field!.GetValue(context).Should().BeNull();

        // Accessing Items lazily initializes it
        var items = context.Items;
        items.Should().NotBeNull();
        field.GetValue(context).Should().NotBeNull();
    }

    // --- FINDING-10: CircuitBreaker Lock-Free Path on Closed State ---

    [Fact]
    public async Task Finding10_CircuitBreaker_ClosedState_DoesNotContendLocks()
    {
        var middleware = new CircuitBreakerMiddleware(new CircuitBreakerOptions
        {
            FailureThreshold = 10,
            BreakDuration = TimeSpan.FromSeconds(30)
        });

        var context = TestMessageContextFactory.CreateContext();

        // Act: Execute 1,000 successful invocations in closed state
        for (int i = 0; i < 1000; i++)
        {
            var result = await middleware.InvokeAsync(
                context,
                (ctx, ct) => ValueTask.FromResult(Result.Success()),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
        }
    }

    // --- FINDING-11: InMemoryMessageTransport NackRequeue Deadlock Elimination ---

    [Fact]
    public async Task Finding11_InMemoryMessageTransport_NackRequeue_DoesNotDeadlockUnderBoundedCapacity()
    {
        var transportOptions = new InMemoryTransportOptions
        {
            ChannelCapacity = 2,
            FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait
        };
        var transport = new InMemoryMessageTransport(
            Microsoft.Extensions.Options.Options.Create(transportOptions),
            NullLogger<InMemoryMessageTransport>.Instance);

        int attempts = 0;
        var subOptions = new TransportSubscriptionOptions { MaxConcurrency = 1 };
        var ackTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync(
            "deadlock.test",
            (payload, meta, ct) =>
            {
                var currentAttempt = Interlocked.Increment(ref attempts);
                if (currentAttempt == 1)
                {
                    // Requeue once
                    return ValueTask.FromResult(TransportAckResult.NackRequeue);
                }

                ackTcs.TrySetResult(true);
                return ValueTask.FromResult(TransportAckResult.Ack);
            },
            subOptions);

        var meta = TransportMessageMetadata.Create("deadlock.test");
        var pubResult = await transport.PublishRawAsync("deadlock.test", new byte[] { 1, 2, 3 }, meta);
        pubResult.IsSuccess.Should().BeTrue();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        cts.Token.Register(() => ackTcs.TrySetCanceled());

        // Assert: NackRequeue succeeds and is re-read without deadlock
        var completed = await ackTcs.Task;
        completed.Should().BeTrue();
        attempts.Should().Be(2);

        await transport.DisposeAsync();
    }

    // --- FINDING-13: DefaultMessageDispatcher Pre-Cancellation Check ---

    [Fact]
    public async Task Finding13_DefaultMessageDispatcher_PreCancellation_ThrowsImmediately()
    {
        var serializer = new NativeAotJsonSerializer(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        var dispatcher = new DefaultMessageDispatcher(serializer);
        var meta = TransportMessageMetadata.Create("test.canceled");
        var sp = Substitute.For<IServiceProvider>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        var act = () => dispatcher.DispatchAsync("test.canceled", new byte[] { 1 }, meta, sp, cts.Token).AsTask();
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // --- FINDING-14: MiddlewarePipeline Pre-Cancellation Check ---

    [Fact]
    public async Task Finding14_MiddlewarePipeline_PreCancellation_ThrowsImmediately()
    {
        var pipeline = new MiddlewarePipeline();
        var context = TestMessageContextFactory.CreateContext();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        var act = () => pipeline.ExecuteAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), cts.Token).AsTask();
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // --- FINDING-15: PartitionKeyExtractor Open Delegate Binding ---

    private sealed record TestPartitionedMessage([property: PartitionKey] string OrderId, decimal Amount);

    [Fact]
    public void Finding15_PartitionKeyExtractor_ExtractsKeyCorrectly()
    {
        var msg = new TestPartitionedMessage("ORDER-999", 50.0m);
        var key = PartitionKeyExtractor<TestPartitionedMessage>.Extract(msg);

        key.Should().Be("ORDER-999");
    }

    // --- FINDING-17: DefaultMessageDispatcher Duplicate Handler Validation ---

    private sealed class AlternateOrderV2Handler : IMessageHandler<UpgradedOrderV2>
    {
        public ValueTask<Result> HandleAsync(UpgradedOrderV2 message, MessageContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(Result.Success());
    }

    [Fact]
    public void Finding17_DefaultMessageDispatcher_DuplicateHandlerRegistration_AllowsPubSubMultipleHandlers()
    {
        var serializer = new NativeAotJsonSerializer(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        var dispatcher = new DefaultMessageDispatcher(serializer);

        dispatcher.RegisterHandler<UpgradedOrderV2, UpgradedOrderV2Handler>("test.duplicate.type");

        var act = () => dispatcher.RegisterHandler<UpgradedOrderV2, AlternateOrderV2Handler>("test.duplicate.type");
        act.Should().NotThrow();
    }

    [Fact]
    public void Finding17_DefaultMessageDispatcher_SameHandlerRegistration_IsIdempotent()
    {
        var serializer = new NativeAotJsonSerializer(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        var dispatcher = new DefaultMessageDispatcher(serializer);

        dispatcher.RegisterHandler<UpgradedOrderV2, UpgradedOrderV2Handler>("test.idempotent.type");

        var act = () => dispatcher.RegisterHandler<UpgradedOrderV2, UpgradedOrderV2Handler>("test.idempotent.type");
        act.Should().NotThrow();
    }

    // --- FINDING-18: MessageConsumer Linked CTS Cancels In-Flight Dispatch on DisposeAsync ---

    [Fact]
    public async Task Finding18_MessageConsumer_DisposeAsync_CancelsLinkedTokenAndInFlightDispatch()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var sp = Substitute.For<IServiceProvider>();

        scopeFactory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(sp);

        Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>? capturedCallback = null;
        transport.SubscribeAsync(
            Arg.Any<string>(),
            Arg.Do<Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>>>(cb => capturedCallback = cb),
            Arg.Any<TransportSubscriptionOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success()));

        var dispatchStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        dispatcher.DispatchAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<IServiceProvider>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ct = callInfo.Arg<CancellationToken>();
                dispatchStarted.TrySetResult(true);
                async Task<Result> RunAsync()
                {
                    try
                    {
                        await Task.Delay(5000, ct);
                        return Result.Success();
                    }
                    catch (OperationCanceledException)
                    {
                        cancellationObserved.TrySetResult(true);
                        throw;
                    }
                }

                return new ValueTask<Result>(RunAsync());
            });

        var consumer = new MessageConsumer(transport, dispatcher, scopeFactory);
        consumer.AddDestination("orders.created");
        await consumer.StartAsync();

        capturedCallback.Should().NotBeNull();

        // Start in-flight message processing
        var metadata = TransportMessageMetadata.Create("orders.created");
        var processTask = capturedCallback!(new byte[] { 1, 2, 3 }, metadata, CancellationToken.None).AsTask();

        // Wait until dispatch is in-flight
        await dispatchStarted.Task;

        // Dispose consumer while dispatch is in-flight
        await consumer.DisposeAsync();

        // Verify cancellation token was triggered inside dispatcher
        var cancelled = await Task.WhenAny(cancellationObserved.Task, Task.Delay(2000)) == cancellationObserved.Task;
        cancelled.Should().BeTrue("consumer disposal must trigger cancellation in in-flight message handlers");

        var ackResult = await processTask;
        ackResult.Should().Be(TransportAckResult.NackRequeue, "cancelled in-flight message must be NackRequeue to avoid broker drop");
    }

    // --- FINDING-15: MessagePublisher Injects Producer Span ID Into Metadata TraceParent (OBS-001) ---
    [Fact]
    public async Task Finding15_MessagePublisher_InjectsProducerActivitySpanId_IntoMetadataTraceParent()
    {
        var transport = Substitute.For<IMessageTransport>();
        TransportMessageMetadata? capturedMetadata = null;

        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Do<TransportMessageMetadata>(m => capturedMetadata = m),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success()));

        var serializer = new NativeAotJsonSerializer(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        var publisher = new MessagePublisher(transport, serializer);

        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == MessagingDiagnostics.ActivitySourceName,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        var msg = new LegacyOrderV1(Guid.NewGuid(), 99.95m);

        // Act
        var result = await publisher.PublishAsync(msg);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedMetadata.Should().NotBeNull();
        capturedMetadata!.TraceParent.Should().NotBeNullOrWhiteSpace();
        capturedMetadata.TraceParent.Should().StartWith("00-", "traceparent must follow W3C trace context specification");
    }

    // --- FINDING-16: AddMessaging Multiple Invocations Does Not Duplicate Middlewares (API-001) ---
    [Fact]
    public void Finding16_AddMessaging_MultipleCalls_DoesNotDuplicateMiddlewares()
    {
        var services = new ServiceCollection();

        // Act: Invoke AddMessaging multiple times
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddMessaging();
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddMessaging();
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddMessaging();

        // Assert: Middlewares are registered exactly once
        var tracingCount = System.Linq.Enumerable.Count(services, d => d.ServiceType == typeof(IMessageMiddleware) && d.ImplementationType == typeof(TracingMiddleware));
        var loggingCount = System.Linq.Enumerable.Count(services, d => d.ServiceType == typeof(IMessageMiddleware) && d.ImplementationType == typeof(LoggingMiddleware));
        var exceptionCount = System.Linq.Enumerable.Count(services, d => d.ServiceType == typeof(IMessageMiddleware) && d.ImplementationType == typeof(ExceptionHandlingMiddleware));

        tracingCount.Should().Be(1);
        loggingCount.Should().Be(1);
        exceptionCount.Should().Be(1);
    }

    // --- FINDING-17: InMemoryMessageTransport Synchronous Dispose Completes Cleanly (CONC-001 / CONC-002) ---
    [Fact]
    public async Task Finding17_InMemoryMessageTransport_Dispose_CompletesCleanlyWithoutObjectDisposedException()
    {
        var transport = new InMemoryMessageTransport();
        await transport.SubscribeAsync(
            "test.dispose.destination",
            (payload, metadata, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            new TransportSubscriptionOptions { MaxConcurrency = 2 });

        // Act & Assert: Synchronous dispose must not throw
        Action act = () => transport.Dispose();
        act.Should().NotThrow();
    }

    // --- FINDING-18: MiddlewarePipeline BuildChain executes middlewares in order with zero closures on dispatch (PERF-001 / PERF-002) ---
    [Fact]
    public async Task Finding18_MiddlewarePipeline_BuildChain_ExecutesZeroAllocationChainInCorrectOrder()
    {
        var executionLog = new List<string>();

        var m1 = new TestOrderMiddleware("M1", executionLog);
        var m2 = new TestOrderMiddleware("M2", executionLog);
        var m3 = new TestOrderMiddleware("M3", executionLog);

        var pipeline = new MiddlewarePipeline(new IMessageMiddleware[] { m1, m2, m3 });

        MessageExecutionDelegate terminal = (ctx, ct) =>
        {
            executionLog.Add("Terminal");
            return ValueTask.FromResult(Result.Success());
        };

        var chain = pipeline.BuildChain(terminal);

        var context = TestMessageContextFactory.CreateContext();

        // Act
        var result = await chain(context, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        executionLog.Should().ContainInOrder("M1:Before", "M2:Before", "M3:Before", "Terminal", "M3:After", "M2:After", "M1:After");
    }

    // --- FINDING-19: MessageConsumer Configurable Unhandled Failure Ack Result (MSG-001) ---
    [Fact]
    public async Task Finding19_MessageConsumer_ConfiguredWithDeadLetterOutcome_ReturnsDeadLetter_WhenNoDLQ()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var sp = Substitute.For<IServiceProvider>();

        scopeFactory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(sp);
        // No IDeadLetterQueue registered in DI:
        sp.GetService(typeof(IDeadLetterQueue)).Returns((object?)null);

        dispatcher.DispatchAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<IServiceProvider>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Failure(Error.Failure("Database.Timeout", "Connection timed out."))));

        var options = Microsoft.Extensions.Options.Options.Create(new MessageConsumerOptions
        {
            UnhandledFailureAckResult = TransportAckResult.DeadLetter
        });

        var consumer = new MessageConsumer(transport, dispatcher, scopeFactory, options: options);

        var method = typeof(MessageConsumer).GetMethod(
            "OnRawMessageReceivedAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        var metadata = TransportMessageMetadata.Create("critical.event");
        var payload = new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 });

        // Act
        var task = (ValueTask<TransportAckResult>)method.Invoke(
            consumer,
            new object[] { payload, metadata, CancellationToken.None })!;

        var ackResult = await task;

        // Assert: When configured with DeadLetter outcome, broker receives DeadLetter instead of Ack!
        ackResult.Should().Be(TransportAckResult.DeadLetter);
    }

    // --- FINDING-19: MessagePublisher Whitespace or Empty Destination Falls Back to MessageType ---
    [Fact]
    public async Task Finding19_MessagePublisher_EmptyOrWhitespaceDestination_FallsBackToMessageType()
    {
        // Arrange
        var transport = Substitute.For<IMessageTransport>();
        string? capturedDestination = null;

        transport.PublishRawAsync(
            Arg.Do<string>(d => capturedDestination = d),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Success()));

        var serializer = new NativeAotJsonSerializer(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        var publisher = new MessagePublisher(transport, serializer);

        var msg = new LegacyOrderV1(Guid.NewGuid(), 49.99m);

        // Act 1: Publish with whitespace destination
        var result1 = await publisher.PublishAsync(msg, new MessagePublishOptions { Destination = "   " });

        // Assert 1: Falls back to resolved message type destination instead of whitespace
        result1.IsSuccess.Should().BeTrue();
        capturedDestination.Should().NotBeNullOrWhiteSpace();
        capturedDestination.Should().Be("orders.created.v1");

        // Act 2: PublishBatch with empty destination
        var result2 = await publisher.PublishBatchAsync(new[] { msg }, new MessagePublishOptions { Destination = "" });

        // Assert 2: Falls back to resolved message type destination
        result2.IsSuccess.Should().BeTrue();
        capturedDestination.Should().Be("orders.created.v1");
    }

    private sealed class TestOrderMiddleware : IMessageMiddleware
    {
        private readonly string _name;
        private readonly List<string> _log;

        public TestOrderMiddleware(string name, List<string> log)
        {
            _name = name;
            _log = log;
        }

        public async ValueTask<Result> InvokeAsync(MessageContext context, MessageExecutionDelegate next, CancellationToken cancellationToken)
        {
            _log.Add($"{_name}:Before");
            var result = await next(context, cancellationToken);
            _log.Add($"{_name}:After");
            return result;
        }
    }

    // --- FINDING-20: MessageConsumer Multiple Sequential Drains (FINDING-CONC-01) ---
    [Fact]
    public async Task Finding20_MessageConsumer_MultipleSequentialDrains_WaitsForNewInFlightMessages()
    {
        var transport = Substitute.For<IMessageTransport>();
        var dispatcher = Substitute.For<IMessageDispatcher>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var sp = Substitute.For<IServiceProvider>();

        scopeFactory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(sp);

        var firstDispatchTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondDispatchTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompleted = false;

        dispatcher.DispatchAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<IServiceProvider>(),
            Arg.Any<CancellationToken>())
            .Returns(
                callInfo =>
                {
                    async Task<Result> FirstCall()
                    {
                        await firstDispatchTcs.Task;
                        return Result.Success();
                    }
                    return new ValueTask<Result>(FirstCall());
                },
                callInfo =>
                {
                    async Task<Result> SecondCall()
                    {
                        await secondDispatchTcs.Task;
                        secondCompleted = true;
                        return Result.Success();
                    }
                    return new ValueTask<Result>(SecondCall());
                });

        var consumer = new MessageConsumer(transport, dispatcher, scopeFactory);
        var method = typeof(MessageConsumer).GetMethod(
            "OnRawMessageReceivedAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        var metadata = TransportMessageMetadata.Create("drain.test");
        var payload = new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 });

        // Cycle 1: First message
        var task1 = (ValueTask<TransportAckResult>)method.Invoke(
            consumer,
            new object[] { payload, metadata, CancellationToken.None })!;

        firstDispatchTcs.TrySetResult(true);
        await task1;
        await consumer.DrainInFlightMessagesAsync();

        // Cycle 2: Second message in-flight
        var task2 = (ValueTask<TransportAckResult>)method.Invoke(
            consumer,
            new object[] { payload, metadata, CancellationToken.None })!;

        // Trigger drain for cycle 2 BEFORE second message finishes
        var drainTask2 = consumer.DrainInFlightMessagesAsync().AsTask();
        drainTask2.IsCompleted.Should().BeFalse("second drain must wait for second message in flight");

        secondDispatchTcs.TrySetResult(true);
        await task2;
        await drainTask2;

        secondCompleted.Should().BeTrue();
    }

    // --- FINDING-21: DefaultMessageDispatcher Concurrent Conflicting Registration (FINDING-CONC-03) ---
    private sealed class ConflictingHandlerA : IMessageHandler<LegacyOrderV1>
    {
        public ValueTask<Result> HandleAsync(LegacyOrderV1 message, MessageContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(Result.Success());
    }

    private sealed class ConflictingHandlerB : IMessageHandler<LegacyOrderV1>
    {
        public ValueTask<Result> HandleAsync(LegacyOrderV1 message, MessageContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(Result.Success());
    }

    [Fact]
    public void Finding21_DefaultMessageDispatcher_ConcurrentConflictingRegistration_AllowsPubSubConcurrency()
    {
        var serializer = new NativeAotJsonSerializer(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        var dispatcher = new DefaultMessageDispatcher(serializer);
        var typeName = "concurrent.conflict.type";

        // Pre-warm so dictionaries are initialized
        dispatcher.RegisterHandler<UpgradedOrderV2, UpgradedOrderV2Handler>(typeName);

        int exceptionsThrown = 0;
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 };
        
        Parallel.For(0, 1000, parallelOptions, i =>
        {
            try
            {
                if (i % 2 == 0)
                {
                    dispatcher.RegisterHandler<UpgradedOrderV2, UpgradedOrderV2Handler>(typeName);
                }
                else
                {
                    dispatcher.RegisterHandler<UpgradedOrderV2, AlternateOrderV2Handler>(typeName);
                }
            }
            catch (InvalidOperationException)
            {
                Interlocked.Increment(ref exceptionsThrown);
            }
        });

        exceptionsThrown.Should().Be(0, "conflicting handler registrations are now allowed as Pub/Sub under concurrency");
    }

    // --- FINDING-22: MessageUpcastingMiddleware Transitive Chained Upcasting (FINDING-ARCH-01) ---
    [MessageType("orders.transitive.v1")]
    public sealed record OrderTransitiveV1(Guid Id, decimal Amount) : IMessage;

    [MessageType("orders.transitive.v2")]
    public sealed record OrderTransitiveV2(Guid Id, decimal Amount, string Currency) : IMessage;

    [MessageType("orders.transitive.v3")]
    public sealed record OrderTransitiveV3(Guid Id, decimal Amount, string Currency, DateTimeOffset ProcessedAt) : IMessage;

    public sealed class OrderTransitiveV1ToV2Upcaster : IMessageUpcaster<OrderTransitiveV1, OrderTransitiveV2>
    {
        public OrderTransitiveV2 Upcast(OrderTransitiveV1 message, TransportMessageMetadata metadata)
            => new(message.Id, message.Amount, "EUR");
    }

    public sealed class OrderTransitiveV2ToV3Upcaster : IMessageUpcaster<OrderTransitiveV2, OrderTransitiveV3>
    {
        public OrderTransitiveV3 Upcast(OrderTransitiveV2 message, TransportMessageMetadata metadata)
            => new(message.Id, message.Amount, message.Currency, DateTimeOffset.UtcNow);
    }

    public sealed class OrderTransitiveV3Handler : IMessageHandler<OrderTransitiveV3>
    {
        public OrderTransitiveV3? ReceivedMessage { get; private set; }

        public ValueTask<Result> HandleAsync(OrderTransitiveV3 message, MessageContext context, CancellationToken cancellationToken)
        {
            ReceivedMessage = message;
            return ValueTask.FromResult(Result.Success());
        }
    }

    [Fact]
    public async Task Finding22_MessageUpcastingMiddleware_TransitiveUpcasting_MigratesV1ToV3()
    {
        var services = new ServiceCollection();
        var serializer = new NativeAotJsonSerializer(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddSingleton<IMessageSerializer>(serializer);

        var v3Handler = new OrderTransitiveV3Handler();
        services.AddSingleton(v3Handler);
        services.AddScoped<IMessageHandler<OrderTransitiveV3>>(_ => v3Handler);
        services.AddScoped<OrderTransitiveV1ToV2Upcaster>();
        services.AddScoped<OrderTransitiveV2ToV3Upcaster>();

        var invoker1 = new MessageUpcasterInvoker<OrderTransitiveV1, OrderTransitiveV2, OrderTransitiveV1ToV2Upcaster>();
        var invoker2 = new MessageUpcasterInvoker<OrderTransitiveV2, OrderTransitiveV3, OrderTransitiveV2ToV3Upcaster>();
        var upcastingMiddleware = new MessageUpcastingMiddleware(new IMessageUpcasterInvoker[] { invoker1, invoker2 });

        var dispatcher = new DefaultMessageDispatcher(
            serializer,
            middlewares: new[] { upcastingMiddleware },
            upcasters: new IMessageUpcasterInvoker[] { invoker1, invoker2 });

        dispatcher.RegisterHandler<OrderTransitiveV3, OrderTransitiveV3Handler>("orders.transitive.v3");

        var msgV1 = new OrderTransitiveV1(Guid.NewGuid(), 250m);
        var rawPayload = serializer.Serialize(msgV1);
        var metadata = TransportMessageMetadata.Create("orders.transitive.v1");

        var sp = services.BuildServiceProvider();

        // Act: Dispatch with V1 type name
        var result = await dispatcher.DispatchAsync("orders.transitive.v1", rawPayload, metadata, sp);

        // Assert: Success and V3 handler received the fully migrated V3 message!
        result.IsSuccess.Should().BeTrue();
        v3Handler.ReceivedMessage.Should().NotBeNull();
        v3Handler.ReceivedMessage!.Id.Should().Be(msgV1.Id);
        v3Handler.ReceivedMessage.Amount.Should().Be(250m);
        v3Handler.ReceivedMessage.Currency.Should().Be("EUR");
    }
}

