// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Tests.Dispatch;

using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Dispatch;
using EricksonLopez.Messaging.Middleware;
using EricksonLopez.Messaging.Serialization;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

[Trait("Category", "Unit")]
public class DefaultMessageDispatcherTests
{
    public sealed record PingMessage(string Value) : IMessage;

    private sealed class PingMessageHandler : IMessageHandler<PingMessage>
    {
        public bool Handled { get; private set; }
        public string? ReceivedValue { get; private set; }

        public ValueTask<Result> HandleAsync(
            PingMessage message,
            MessageContext context,
            CancellationToken cancellationToken = default)
        {
            Handled = true;
            ReceivedValue = message.Value;
            return ValueTask.FromResult(Result.Success());
        }
    }

    private sealed class FailingHandler : IMessageHandler<PingMessage>
    {
        public ValueTask<Result> HandleAsync(
            PingMessage message,
            MessageContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Failure(Error.Validation("Test.Failure", "Intentional validation failure.")));
        }
    }

    private sealed class DummyRegistration : IHandlerRegistration
    {
        public string TypeName => "ping.registered";
        public bool Registered { get; private set; }
        public void Register(DefaultMessageDispatcher dispatcher)
        {
            Registered = true;
            dispatcher.RegisterHandler<PingMessage, PingMessageHandler>("ping.registered");
        }
    }

    [Fact]
    public void Constructor_NullSerializer_ThrowsArgumentNullException()
    {
        Action act = () => new DefaultMessageDispatcher(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serializer");
    }

    [Fact]
    public void Constructor_WithBindingsAndRegistrations_InitializesProperly()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var registration = new DummyRegistration();
        var initialBindings = new Dictionary<string, IReadOnlyList<DefaultMessageDispatcher.HandlerBinding>>
        {
            ["existing.v1"] = new List<DefaultMessageDispatcher.HandlerBinding> { new(typeof(PingMessage), typeof(PingMessageHandler), static (sp, msg, ctx, ct) => ValueTask.FromResult(Result.Success())) }
        };

        var dispatcher = new DefaultMessageDispatcher(
            serializer: serializer,
            middlewares: null,
            bindings: initialBindings,
            registrations: new[] { registration });

        registration.Registered.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RegisterHandler_InvalidTypeName_ThrowsArgumentException(string? invalidTypeName)
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var dispatcher = new DefaultMessageDispatcher(serializer);

        Action act = () => dispatcher.RegisterHandler<PingMessage, PingMessageHandler>(invalidTypeName!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task DispatchAsync_ArgumentValidation_ThrowsAppropriateExceptions()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var dispatcher = new DefaultMessageDispatcher(serializer);
        var metadata = TransportMessageMetadata.Create("test");
        var sp = Substitute.For<IServiceProvider>();

        Func<Task> act1 = async () => await dispatcher.DispatchAsync(null!, new byte[] { 1 }, metadata, sp);
        await act1.Should().ThrowAsync<ArgumentException>();

        Func<Task> act2 = async () => await dispatcher.DispatchAsync("", new byte[] { 1 }, metadata, sp);
        await act2.Should().ThrowAsync<ArgumentException>();

        Func<Task> act3 = async () => await dispatcher.DispatchAsync("test", new byte[] { 1 }, null!, sp);
        await act3.Should().ThrowAsync<ArgumentNullException>().WithParameterName("metadata");

        Func<Task> act4 = async () => await dispatcher.DispatchAsync("test", new byte[] { 1 }, metadata, null!);
        await act4.Should().ThrowAsync<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public async Task DispatchAsync_DeserializationThrows_ReturnsValidationFailure()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Deserialize(Arg.Any<ReadOnlyMemory<byte>>(), typeof(PingMessage))
            .Throws(new InvalidOperationException("Corrupt binary stream"));

        var dispatcher = new DefaultMessageDispatcher(serializer);
        dispatcher.RegisterHandler<PingMessage, PingMessageHandler>("ping.v1");

        var metadata = TransportMessageMetadata.Create("ping.v1");
        var sp = Substitute.For<IServiceProvider>();

        // Act
        var result = await dispatcher.DispatchAsync("ping.v1", new byte[] { 1 }, metadata, sp);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Messaging.DeserializationFailed");
        result.Error.Description.Should().Contain("Corrupt binary stream");
    }

    [Fact]
    public async Task DispatchAsync_RegisteredHandler_InvokesSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<PingMessageHandler>();
        var sp = services.BuildServiceProvider();

        var serializer = new NativeAotJsonSerializer(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        var dispatcher = new DefaultMessageDispatcher(serializer);
        dispatcher.RegisterHandler<PingMessage, PingMessageHandler>("ping.v1");

        var payload = serializer.Serialize(new PingMessage("Hello World"));
        var metadata = TransportMessageMetadata.Create("ping.v1");

        // Act
        using var scope = sp.CreateScope();
        var result = await dispatcher.DispatchAsync("ping.v1", payload, metadata, scope.ServiceProvider);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var handler = scope.ServiceProvider.GetRequiredService<PingMessageHandler>();
        handler.Handled.Should().BeTrue();
        handler.ReceivedValue.Should().Be("Hello World");
    }

    [Fact]
    public async Task DispatchAsync_UnregisteredMessageType_ReturnsNotFoundFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        var serializer = new NativeAotJsonSerializer(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        var dispatcher = new DefaultMessageDispatcher(serializer);

        var payload = serializer.Serialize(new PingMessage("Hello"));
        var metadata = TransportMessageMetadata.Create("unknown.type");

        // Act
        using var scope = sp.CreateScope();
        var result = await dispatcher.DispatchAsync("unknown.type", payload, metadata, scope.ServiceProvider);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Messaging.HandlerNotFound");
    }

    [Fact]
    public async Task DispatchAsync_HandlerReturnsFailure_PropagatesResultFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<FailingHandler>();
        var sp = services.BuildServiceProvider();

        var serializer = new NativeAotJsonSerializer(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        var dispatcher = new DefaultMessageDispatcher(serializer);
        dispatcher.RegisterHandler<PingMessage, FailingHandler>("failing.v1");

        var payload = serializer.Serialize(new PingMessage("Fail"));
        var metadata = TransportMessageMetadata.Create("failing.v1");

        // Act
        using var scope = sp.CreateScope();
        var result = await dispatcher.DispatchAsync("failing.v1", payload, metadata, scope.ServiceProvider);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Test.Failure");
    }

    [Fact]
    public void HandlerBinding_RecordPropertiesAndEquality_WorkCorrectly()
    {
        Func<IServiceProvider, object, MessageContext, CancellationToken, ValueTask<Result>> invoker =
            static (sp, msg, ctx, ct) => ValueTask.FromResult(Result.Success());

        var b1 = new DefaultMessageDispatcher.HandlerBinding(typeof(PingMessage), typeof(PingMessageHandler), invoker);
        var b2 = new DefaultMessageDispatcher.HandlerBinding(typeof(PingMessage), typeof(PingMessageHandler), invoker);
        var b3 = new DefaultMessageDispatcher.HandlerBinding(typeof(string), typeof(PingMessageHandler), invoker);

        b1.MessageType.Should().Be(typeof(PingMessage));
        b1.HandlerType.Should().Be(typeof(PingMessageHandler));
        b1.Invoker.Should().BeSameAs(invoker);

        b1.Should().Be(b2);
        b1.Should().NotBe(b3);
        b1.GetHashCode().Should().Be(b2.GetHashCode());
    }

    private sealed class ConcreteHandlerRegistration : HandlerRegistrationBase
    {
        public bool Registered;
        public ConcreteHandlerRegistration(string typeName) : base(typeName) { }
        public override void Register(DefaultMessageDispatcher dispatcher)
        {
            Registered = true;
        }
    }

    [Fact]
    public void HandlerRegistrationBase_PropertiesAndRegistration_WorkCorrectly()
    {
        var reg = new ConcreteHandlerRegistration("test.msg.v1");
        reg.TypeName.Should().Be("test.msg.v1");

        var serializer = Substitute.For<IMessageSerializer>();
        var dispatcher = new DefaultMessageDispatcher(serializer);
        reg.Register(dispatcher);
        reg.Registered.Should().BeTrue();
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
    public void DispatchAsync_ConfiguresAwaitFalse_DoesNotCaptureSynchronizationContext()
    {
        var syncContext = new TrackingSynchronizationContext();
        var prevContext = SynchronizationContext.Current;
        try
        {
            var tcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
            var services = new ServiceCollection();
            var sp = services.BuildServiceProvider();

            var serializer = Substitute.For<IMessageSerializer>();
            serializer.Deserialize(Arg.Any<ReadOnlyMemory<byte>>(), typeof(PingMessage)).Returns(new PingMessage("Hello"));

            var initialBindings = new Dictionary<string, IReadOnlyList<DefaultMessageDispatcher.HandlerBinding>>
            {
                ["ping.v1"] = new List<DefaultMessageDispatcher.HandlerBinding> { new(typeof(PingMessage), typeof(PingMessageHandler), (s, msg, ctx, ct) => new ValueTask<Result>(tcs.Task)) }
            };

            var dispatcher = new DefaultMessageDispatcher(serializer, bindings: initialBindings);
            var metadata = TransportMessageMetadata.Create("ping.v1");

            SynchronizationContext.SetSynchronizationContext(syncContext);

            var valueTask = dispatcher.DispatchAsync("ping.v1", new byte[] { 1 }, metadata, sp);

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

    private sealed class AsyncPingHandler : IMessageHandler<PingMessage>
    {
        public TaskCompletionSource<Result> Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<Result> HandleAsync(
            PingMessage message,
            MessageContext context,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<Result>(Tcs.Task);
        }
    }

    [Fact]
    public void DispatchAsync_RegisteredHandler_ConfiguresAwaitFalse_DoesNotCaptureSynchronizationContext()
    {
        var syncContext = new TrackingSynchronizationContext();
        var prevContext = SynchronizationContext.Current;
        try
        {
            var handler = new AsyncPingHandler();
            var services = new ServiceCollection();
            services.AddSingleton(handler);
            var sp = services.BuildServiceProvider();

            var serializer = new NativeAotJsonSerializer(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
            var dispatcher = new DefaultMessageDispatcher(serializer);
            dispatcher.RegisterHandler<PingMessage, AsyncPingHandler>("ping.v1");

            var payload = serializer.Serialize(new PingMessage("Hello"));
            var metadata = TransportMessageMetadata.Create("ping.v1");

            SynchronizationContext.SetSynchronizationContext(syncContext);

            var valueTask = dispatcher.DispatchAsync("ping.v1", payload, metadata, sp);

            SynchronizationContext.SetSynchronizationContext(prevContext);

#pragma warning disable xUnit1031
            Task.Run(() => handler.Tcs.SetResult(Result.Success())).Wait();
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

    private sealed class MutatingPingMiddleware : IMessageMiddleware
    {
        public ValueTask<Result> InvokeAsync(
            MessageContext context,
            MessageExecutionDelegate next,
            CancellationToken cancellationToken = default)
        {
            context.Message = new PingMessage("UpcastedValue");
            return next(context, cancellationToken);
        }
    }

    [Fact]
    public async Task DispatchAsync_WhenPipelineMutatesContextMessage_PassesMutatedMessageToHandler()
    {
        var handler = new PingMessageHandler();
        var services = new ServiceCollection();
        services.AddSingleton(handler);
        var sp = services.BuildServiceProvider();

        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Deserialize(Arg.Any<ReadOnlyMemory<byte>>(), typeof(PingMessage))
            .Returns(new PingMessage("OriginalValue"));

        var middlewares = new IMessageMiddleware[] { new MutatingPingMiddleware() };
        var dispatcher = new DefaultMessageDispatcher(serializer, middlewares: middlewares);
        dispatcher.RegisterHandler<PingMessage, PingMessageHandler>("ping.v1");

        var metadata = TransportMessageMetadata.Create("ping.v1");
        var result = await dispatcher.DispatchAsync("ping.v1", new byte[] { 1, 2, 3 }, metadata, sp);

        result.IsSuccess.Should().BeTrue();
        handler.Handled.Should().BeTrue();
        handler.ReceivedValue.Should().Be("UpcastedValue");
    }

    [Fact]
    public async Task DispatchBatchAsync_NullItems_ThrowsArgumentNullException()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var dispatcher = new DefaultMessageDispatcher(serializer);
        var sp = new ServiceCollection().BuildServiceProvider();

        Func<Task> act = async () => await dispatcher.DispatchBatchAsync(null!, sp);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("items");
    }

    [Fact]
    public async Task DispatchBatchAsync_NullServiceProvider_ThrowsArgumentNullException()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var dispatcher = new DefaultMessageDispatcher(serializer);

        Func<Task> act = async () => await dispatcher.DispatchBatchAsync(Array.Empty<MessageDispatchItem>(), null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public async Task DispatchBatchAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var dispatcher = new DefaultMessageDispatcher(serializer);
        var sp = new ServiceCollection().BuildServiceProvider();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var items = new[] { new MessageDispatchItem("ping.v1", new byte[] { 1 }, TransportMessageMetadata.Create("ping.v1")) };
        Func<Task> act = async () => await dispatcher.DispatchBatchAsync(items, sp, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DispatchAsync_MultipleHandlersForSameMessageType_WhenOneFails_ReturnsFailureWithAggregatedError()
    {
        var pingHandler = new PingMessageHandler();
        var failingHandler = new FailingHandler();
        var services = new ServiceCollection();
        services.AddSingleton(pingHandler);
        services.AddSingleton(failingHandler);
        var sp = services.BuildServiceProvider();

        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Deserialize(Arg.Any<ReadOnlyMemory<byte>>(), typeof(PingMessage))
            .Returns(new PingMessage("MultiTest"));

        var dispatcher = new DefaultMessageDispatcher(serializer);
        dispatcher.RegisterHandler<PingMessage, PingMessageHandler>("ping.multi");
        dispatcher.RegisterHandler<PingMessage, FailingHandler>("ping.multi");

        var metadata = TransportMessageMetadata.Create("ping.multi");
        var result = await dispatcher.DispatchAsync("ping.multi", new byte[] { 1 }, metadata, sp);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Messaging.DispatchFailed");
        pingHandler.Handled.Should().BeTrue();
    }

    [Fact]
    public void RegisterHandler_DuplicateRegistration_DoesNotAddSecondBinding()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var dispatcher = new DefaultMessageDispatcher(serializer);

        dispatcher.RegisterHandler<PingMessage, PingMessageHandler>("ping.dup");
        dispatcher.RegisterHandler<PingMessage, PingMessageHandler>("ping.dup");

        var field = typeof(DefaultMessageDispatcher).GetField("_bindings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var bindings = (System.Collections.IDictionary)field!.GetValue(dispatcher)!;
        var list = (System.Collections.IList)bindings["ping.dup"]!;
        list.Count.Should().Be(1);
    }

    public sealed record PongMessage(string Content) : IMessage;

    private sealed class PingMessageSecondHandler : IMessageHandler<PingMessage>
    {
        public bool Handled { get; private set; }
        public ValueTask<Result> HandleAsync(PingMessage message, MessageContext context, CancellationToken cancellationToken = default)
        {
            Handled = true;
            return ValueTask.FromResult(Result.Success());
        }
    }

    [Fact]
    public async Task DispatchAsync_MultipleHandlers_WhenAllSucceed_ReturnsSuccess()
    {
        var handler1 = new PingMessageHandler();
        var handler2 = new PingMessageSecondHandler();
        var services = new ServiceCollection();
        services.AddSingleton(handler1);
        services.AddSingleton(handler2);
        var sp = services.BuildServiceProvider();

        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Deserialize(Arg.Any<ReadOnlyMemory<byte>>(), typeof(PingMessage))
            .Returns(new PingMessage("multi-success"));

        var dispatcher = new DefaultMessageDispatcher(serializer);
        dispatcher.RegisterHandler<PingMessage, PingMessageHandler>("ping.multi.success");
        dispatcher.RegisterHandler<PingMessage, PingMessageSecondHandler>("ping.multi.success");

        var metadata = TransportMessageMetadata.Create("ping.multi.success");
        var result = await dispatcher.DispatchAsync("ping.multi.success", new byte[] { 1 }, metadata, sp);

        result.IsSuccess.Should().BeTrue();
        handler1.Handled.Should().BeTrue();
        handler2.Handled.Should().BeTrue();
    }

    [Fact]
    public async Task DispatchAsync_WhenUpcasterRegisteredWithoutTargetHandler_ReturnsHandlerNotFound()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var upcaster = Substitute.For<IMessageUpcasterInvoker>();
        upcaster.SourceType.Returns(typeof(PingMessage));
        upcaster.TargetType.Returns(typeof(PongMessage));

        var dispatcher = new DefaultMessageDispatcher(serializer, upcasters: [upcaster]);
        var sp = new ServiceCollection().BuildServiceProvider();
        var metadata = TransportMessageMetadata.Create("PingMessage");

        var result = await dispatcher.DispatchAsync("PingMessage", new byte[] { 1 }, metadata, sp);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Messaging.HandlerNotFound");
        result.Error.Description.Should().Contain("No registered message handler found for message type identifier 'PingMessage'");
    }

    [Fact]
    public async Task DispatchBatchAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var dispatcher = new DefaultMessageDispatcher(serializer);
        var sp = new ServiceCollection().BuildServiceProvider();
        var items = new List<MessageDispatchItem>
        {
            new("test", new byte[] { 1 }, TransportMessageMetadata.Create("test"))
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await dispatcher.DispatchBatchAsync(items, sp, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    public sealed class PongMessageHandler : IMessageHandler<PongMessage>
    {
        public bool Handled { get; private set; }
        public ValueTask<Result> HandleAsync(PongMessage message, MessageContext context, CancellationToken cancellationToken = default)
        {
            Handled = true;
            return ValueTask.FromResult(Result.Success());
        }
    }

    public sealed class CrashHandler : IMessageHandler<PingMessage>
    {
        public ValueTask<Result> HandleAsync(PingMessage message, MessageContext context, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("CrashBoomBang");
        }
    }

    public sealed class CancellingPingHandler(CancellationTokenSource cts) : IMessageHandler<PingMessage>
    {
        public ValueTask<Result> HandleAsync(PingMessage message, MessageContext context, CancellationToken cancellationToken = default)
        {
            cts.Cancel();
            return ValueTask.FromResult(Result.Success());
        }
    }

    [Fact]
    public async Task Constructor_WithBindingsHavingNullExecutionChain_BuildsExecutionChainAndDispatches()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Deserialize(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<Type>()).Returns(new PingMessage("msg-1"));

        var handled = false;
        var binding = new DefaultMessageDispatcher.HandlerBinding(
            MessageType: typeof(PingMessage),
            HandlerType: typeof(PingMessageHandler),
            Invoker: (sp, msg, ctx, ct) =>
            {
                handled = true;
                return ValueTask.FromResult(Result.Success());
            });

        var bindingsDict = new Dictionary<string, IReadOnlyList<DefaultMessageDispatcher.HandlerBinding>>
        {
            ["ping.ctor"] = new List<DefaultMessageDispatcher.HandlerBinding> { binding }
        };

        var dispatcher = new DefaultMessageDispatcher(serializer, bindings: bindingsDict);
        var sp = new ServiceCollection().BuildServiceProvider();
        var metadata = TransportMessageMetadata.Create("ping.ctor");

        var result = await dispatcher.DispatchAsync("ping.ctor", new byte[] { 1 }, metadata, sp);

        result.IsSuccess.Should().BeTrue();
        handled.Should().BeTrue();
    }

    [Fact]
    public async Task DispatchAsync_WhenUpcasterRegistered_ResolvesUsingSourceTypeFullName()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Deserialize(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<Type>()).Returns(new PingMessage("v1"));

        var handler = new PongMessageHandler();
        var services = new ServiceCollection();
        services.AddSingleton(handler);
        var sp = services.BuildServiceProvider();

        var upcaster = Substitute.For<IMessageUpcasterInvoker>();
        upcaster.SourceType.Returns(typeof(PingMessage));
        upcaster.TargetType.Returns(typeof(PongMessage));
        upcaster.Upcast(Arg.Any<object>(), Arg.Any<TransportMessageMetadata>(), Arg.Any<IServiceProvider>())
            .Returns(new PongMessage("v2"));

        var dispatcher = new DefaultMessageDispatcher(serializer, middlewares: [new MessageUpcastingMiddleware([upcaster])], upcasters: [upcaster]);
        dispatcher.RegisterHandler<PongMessage, PongMessageHandler>("PongMessage");

        var fullName = typeof(PingMessage).FullName!;
        var metadata = TransportMessageMetadata.Create(fullName);
        var result = await dispatcher.DispatchAsync(fullName, new byte[] { 1 }, metadata, sp);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DispatchAsync_WhenHandlerThrowsUnhandledException_ReturnsExpectedErrorCodeAndDescription()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Deserialize(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<Type>()).Returns(new PingMessage("boom"));

        var services = new ServiceCollection();
        services.AddSingleton<CrashHandler>();
        var sp = services.BuildServiceProvider();

        var dispatcher = new DefaultMessageDispatcher(serializer);
        dispatcher.RegisterHandler<PingMessage, CrashHandler>("ping.throw");

        var metadata = TransportMessageMetadata.Create("ping.throw");
        var result = await dispatcher.DispatchAsync("ping.throw", new byte[] { 1 }, metadata, sp);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Messaging.HandlerUnhandledException");
        result.Error.Description.Should().Be($"Unhandled exception in handler {nameof(CrashHandler)}: CrashBoomBang");
    }

    [Fact]
    public async Task DispatchAsync_MultiHandler_WhenCancelledBetweenHandlers_ThrowsOperationCanceledException()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Deserialize(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<Type>()).Returns(new PingMessage("multi"));

        using var cts = new CancellationTokenSource();
        var handler2 = new PingMessageSecondHandler();

        var services = new ServiceCollection();
        services.AddSingleton(new CancellingPingHandler(cts));
        services.AddSingleton(handler2);
        var sp = services.BuildServiceProvider();

        var dispatcher = new DefaultMessageDispatcher(serializer);
        dispatcher.RegisterHandler<PingMessage, CancellingPingHandler>("ping.cancel");
        dispatcher.RegisterHandler<PingMessage, PingMessageSecondHandler>("ping.cancel");

        var metadata = TransportMessageMetadata.Create("ping.cancel");
        Func<Task> act = async () => await dispatcher.DispatchAsync("ping.cancel", new byte[] { 1 }, metadata, sp, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler2.Handled.Should().BeFalse();
    }

    [Fact]
    public async Task DispatchBatchAsync_WhenCancelledDuringBatch_ThrowsOperationCanceledExceptionAndStops()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Deserialize(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<Type>()).Returns(new PingMessage("batch"));

        using var cts = new CancellationTokenSource();
        int executedCount = 0;

        var handler = Substitute.For<IMessageHandler<PingMessage>>();
        handler.HandleAsync(Arg.Any<PingMessage>(), Arg.Any<MessageContext>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                executedCount++;
                cts.Cancel();
                return ValueTask.FromResult(Result.Success());
            });

        var services = new ServiceCollection();
        services.AddSingleton(handler);
        var sp = services.BuildServiceProvider();

        var dispatcher = new DefaultMessageDispatcher(serializer);
        dispatcher.RegisterHandler<PingMessage, IMessageHandler<PingMessage>>("ping.batch");

        var items = new List<MessageDispatchItem>
        {
            new("ping.batch", new byte[] { 1 }, TransportMessageMetadata.Create("ping.batch")),
            new("ping.batch", new byte[] { 2 }, TransportMessageMetadata.Create("ping.batch"))
        };

        Func<Task> act = async () => await dispatcher.DispatchBatchAsync(items, sp, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        executedCount.Should().Be(1);
    }
}





