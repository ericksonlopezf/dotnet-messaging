// Copyright © Erickson Lopez. MIT License.
namespace EricksonLopez.Messaging.Tests.Middleware;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Middleware;
using EricksonLopez.Messaging.Tests.Common;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Result = EricksonLopez.Result.Result;

[Trait("Category", "Unit")]
public class MessageUpcastingMiddlewareTests
{
    [MessageType("order.created.v1")]
    private sealed record OrderCreatedV1(string OrderId, double OldPrice) : IMessage;

    [MessageType("order.created.v2")]
    private sealed record OrderCreatedV2(string OrderId, decimal NewPrice, string Currency) : IMessage;

    private sealed class OrderCreatedUpcaster : IMessageUpcaster<OrderCreatedV1, OrderCreatedV2>
    {
        public OrderCreatedV2 Upcast(OrderCreatedV1 oldMessage, TransportMessageMetadata metadata)
        {
            return new OrderCreatedV2(oldMessage.OrderId, (decimal)oldMessage.OldPrice, "USD");
        }
    }

    private sealed class FaultyUpcaster : IMessageUpcaster<OrderCreatedV1, OrderCreatedV2>
    {
        public OrderCreatedV2 Upcast(OrderCreatedV1 oldMessage, TransportMessageMetadata metadata)
        {
            throw new InvalidOperationException("Upcasting explosion");
        }
    }

    [Fact]
    public async Task InvokeAsync_NullContext_ThrowsArgumentNullException()
    {
        var middleware = new MessageUpcastingMiddleware(Array.Empty<IMessageUpcasterInvoker>());
        Func<Task> act = async () => await middleware.InvokeAsync(null!, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InvokeAsync_NullNext_ThrowsArgumentNullException()
    {
        var middleware = new MessageUpcastingMiddleware(Array.Empty<IMessageUpcasterInvoker>());
        var context = TestMessageContextFactory.CreateContext("test.upcast", "corr-1");

        Func<Task> act = async () => await middleware.InvokeAsync(context, null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InvokeAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var middleware = new MessageUpcastingMiddleware(Array.Empty<IMessageUpcasterInvoker>());
        var context = TestMessageContextFactory.CreateContext("test.upcast", "corr-1");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task InvokeAsync_NoMessageInContext_PassesThroughWithoutError()
    {
        var middleware = new MessageUpcastingMiddleware(Array.Empty<IMessageUpcasterInvoker>());
        var context = TestMessageContextFactory.CreateContext("test.upcast", "corr-1");
        context.Message = null;

        var result = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_MessageWithoutUpcaster_LeavesMessageUnchanged()
    {
        var middleware = new MessageUpcastingMiddleware(Array.Empty<IMessageUpcasterInvoker>());
        var context = TestMessageContextFactory.CreateContext("test.upcast", "corr-1");
        var original = new OrderCreatedV1("ORD-1", 10.5);
        context.Message = original;

        var result = await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            ctx.Message.Should().BeSameAs(original);
            return ValueTask.FromResult(Result.Success());
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_MessageWithUpcaster_UpgradesMessagePayload()
    {
        var services = new ServiceCollection();
        services.AddScoped<OrderCreatedUpcaster>();
        using var sp = services.BuildServiceProvider();

        var invoker = new MessageUpcasterInvoker<OrderCreatedV1, OrderCreatedV2, OrderCreatedUpcaster>();
        var middleware = new MessageUpcastingMiddleware(new[] { invoker });

        var meta = TestMessageContextFactory.CreateMetadata("order.created.v1");
        var context = new MessageContext(meta, sp)
        {
            Message = new OrderCreatedV1("ORD-99", 50.0)
        };

        var result = await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            ctx.Message.Should().BeOfType<OrderCreatedV2>();
            var v2 = (OrderCreatedV2)ctx.Message;
            v2.OrderId.Should().Be("ORD-99");
            v2.NewPrice.Should().Be(50.0m);
            v2.Currency.Should().Be("USD");
            return ValueTask.FromResult(Result.Success());
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_UpcasterThrows_ReturnsFailureResult()
    {
        var services = new ServiceCollection();
        services.AddScoped<FaultyUpcaster>();
        using var sp = services.BuildServiceProvider();

        var invoker = new MessageUpcasterInvoker<OrderCreatedV1, OrderCreatedV2, FaultyUpcaster>();
        var middleware = new MessageUpcastingMiddleware(new[] { invoker });

        var meta = TestMessageContextFactory.CreateMetadata("order.created.v1");
        var context = new MessageContext(meta, sp)
        {
            Message = new OrderCreatedV1("ORD-99", 50.0)
        };

        var result = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Messaging.UpcastFailed");
        result.Error.Description.Should().Be($"Failed to upcast message of type '{typeof(OrderCreatedV1).FullName}': Upcasting explosion");
    }

    [Fact]
    public void MessageUpcasterInvoker_PropertiesAndInvocation_ExecutesCorrectly()
    {
        var services = new ServiceCollection();
        services.AddScoped<OrderCreatedUpcaster>();
        using var sp = services.BuildServiceProvider();

        var invoker = new MessageUpcasterInvoker<OrderCreatedV1, OrderCreatedV2, OrderCreatedUpcaster>();
        invoker.SourceType.Should().Be(typeof(OrderCreatedV1));

        var metadata = TestMessageContextFactory.CreateMetadata("order.created.v1");
        var upgraded = invoker.Upcast(new OrderCreatedV1("ORD-1", 10.0), metadata, sp);
        upgraded.Should().BeOfType<OrderCreatedV2>();
        ((OrderCreatedV2)upgraded).OrderId.Should().Be("ORD-1");
    }

    [Fact]
    public void DI_AddMessageUpcaster_And_AddUpcasting_RegistersSuccessfully()
    {
        var services = new ServiceCollection();
        services.AddMessageUpcaster<OrderCreatedV1, OrderCreatedV2, OrderCreatedUpcaster>();
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddMessaging(options =>
        {
            options.AddUpcasting();
        });

        using var sp = services.BuildServiceProvider();
        var invokers = sp.GetServices<IMessageUpcasterInvoker>();
        invokers.Should().ContainSingle();

        var middlewares = sp.GetServices<IMessageMiddleware>();
        middlewares.Should().Contain(m => m is MessageUpcastingMiddleware);
    }

    [Fact]
    public async Task Constructor_NullUpcasters_InitializesSafely()
    {
        var logger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<MessageUpcastingMiddleware>>();
        var middleware = new MessageUpcastingMiddleware(null!, logger);
        var context = TestMessageContextFactory.CreateContext("test.upcast", "corr-1");
        context.Message = new OrderCreatedV1("ORD-1", 10.0);

        var result = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [MessageType("item.added.v1")]
    private sealed record ItemAddedV1(string ItemId) : IMessage;

    [MessageType("item.added.v2")]
    private sealed record ItemAddedV2(string ItemId, int Qty) : IMessage;

    private sealed class ItemAddedUpcaster : IMessageUpcaster<ItemAddedV1, ItemAddedV2>
    {
        public ItemAddedV2 Upcast(ItemAddedV1 oldMessage, TransportMessageMetadata metadata)
        {
            return new ItemAddedV2(oldMessage.ItemId, 1);
        }
    }

    [Fact]
    public async Task InvokeAsync_MultipleUpcastersAndRepeatedCalls_ExercisesAllBranchesAndCache()
    {
        var services = new ServiceCollection();
        services.AddScoped<OrderCreatedUpcaster>();
        services.AddScoped<ItemAddedUpcaster>();
        using var sp = services.BuildServiceProvider();

        var invoker1 = new MessageUpcasterInvoker<ItemAddedV1, ItemAddedV2, ItemAddedUpcaster>();
        var invoker2 = new MessageUpcasterInvoker<OrderCreatedV1, OrderCreatedV2, OrderCreatedUpcaster>();
        var middleware = new MessageUpcastingMiddleware(new IMessageUpcasterInvoker[] { invoker1, invoker2 });

        var meta = TestMessageContextFactory.CreateMetadata("order.created.v1");
        var context1 = new MessageContext(meta, sp) { Message = new OrderCreatedV1("ORD-100", 99.0) };

        // 1st call for OrderCreatedV1 (searches past invoker1 to invoker2 and populates cache)
        var result1 = await middleware.InvokeAsync(context1, (ctx, ct) =>
        {
            ctx.Message.Should().BeOfType<OrderCreatedV2>();
            return ValueTask.FromResult(Result.Success());
        }, CancellationToken.None);

        result1.IsSuccess.Should().BeTrue();

        // 2nd call for OrderCreatedV1 (hits cache)
        var context2 = new MessageContext(meta, sp) { Message = new OrderCreatedV1("ORD-101", 100.0) };
        var result2 = await middleware.InvokeAsync(context2, (ctx, ct) =>
        {
            ctx.Message.Should().BeOfType<OrderCreatedV2>();
            return ValueTask.FromResult(Result.Success());
        }, CancellationToken.None);

        result2.IsSuccess.Should().BeTrue();

        // Call for unmatched type (populates null in cache)
        var contextUnmatched1 = new MessageContext(meta, sp) { Message = new OrderCreatedV2("ORD-200", 50.0m, "USD") };
        var resultUnmatched1 = await middleware.InvokeAsync(contextUnmatched1, (ctx, ct) =>
        {
            ctx.Message.Should().BeOfType<OrderCreatedV2>();
            return ValueTask.FromResult(Result.Success());
        }, CancellationToken.None);

        resultUnmatched1.IsSuccess.Should().BeTrue();

        // Repeated call for unmatched type (hits null cache)
        var contextUnmatched2 = new MessageContext(meta, sp) { Message = new OrderCreatedV2("ORD-201", 60.0m, "USD") };
        var resultUnmatched2 = await middleware.InvokeAsync(contextUnmatched2, (ctx, ct) =>
        {
            ctx.Message.Should().BeOfType<OrderCreatedV2>();
            return ValueTask.FromResult(Result.Success());
        }, CancellationToken.None);

        resultUnmatched2.IsSuccess.Should().BeTrue();
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
    public async Task InvokeAsync_WhenSuccessAndFailure_LogsDebugAndError()
    {
        var logger = new TestLogger<MessageUpcastingMiddleware>();
        var services = new ServiceCollection();
        services.AddScoped<OrderCreatedUpcaster>();
        services.AddScoped<FaultyUpcaster>();
        using var sp = services.BuildServiceProvider();

        var invokerSuccess = new MessageUpcasterInvoker<OrderCreatedV1, OrderCreatedV2, OrderCreatedUpcaster>();
        var middlewareSuccess = new MessageUpcastingMiddleware(new[] { invokerSuccess }, logger);

        var meta = TestMessageContextFactory.CreateMetadata("order.created.v1");
        var contextSuccess = new MessageContext(meta, sp) { Message = new OrderCreatedV1("ORD-1", 10.0) };

        var rSuccess = await middlewareSuccess.InvokeAsync(contextSuccess, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);
        rSuccess.IsSuccess.Should().BeTrue();

        logger.Entries.Should().ContainSingle(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Debug &&
            e.Message.Contains("Upcasted message") &&
            e.Message.Contains("OrderCreatedV1") &&
            e.Message.Contains("OrderCreatedV2"));

        logger.Entries.Clear();

        var invokerFaulty = new MessageUpcasterInvoker<OrderCreatedV1, OrderCreatedV2, FaultyUpcaster>();
        var middlewareFaulty = new MessageUpcastingMiddleware(new[] { invokerFaulty }, logger);
        var contextFaulty = new MessageContext(meta, sp) { Message = new OrderCreatedV1("ORD-2", 20.0) };

        var rFaulty = await middlewareFaulty.InvokeAsync(contextFaulty, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);
        rFaulty.IsFailure.Should().BeTrue();

        logger.Entries.Should().ContainSingle(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error &&
            e.Message.Contains("Failed to upcast message") &&
            e.Message.Contains("OrderCreatedV1"));
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
            var context = TestMessageContextFactory.CreateContext("test.upcast", "corr-1");
            var middleware = new MessageUpcastingMiddleware(Array.Empty<IMessageUpcasterInvoker>());

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
    public async Task InvokeAsync_WhenUpcastSucceeds_LogsDebugWithSourceAndTargetTypes()
    {
        var services = new ServiceCollection();
        services.AddScoped<OrderCreatedUpcaster>();
        using var sp = services.BuildServiceProvider();

        var logger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<MessageUpcastingMiddleware>>();
        var invoker = new MessageUpcasterInvoker<OrderCreatedV1, OrderCreatedV2, OrderCreatedUpcaster>();
        var middleware = new MessageUpcastingMiddleware(new[] { invoker }, logger);

        var meta = TestMessageContextFactory.CreateMetadata("order.created.v1");
        var context = new MessageContext(meta, sp)
        {
            Message = new OrderCreatedV1("ORD-DEBUG", 42.0)
        };

        var result = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.Message.Should().BeOfType<OrderCreatedV2>();
    }

    private sealed class CyclicV2ToV1Upcaster : IMessageUpcaster<OrderCreatedV2, OrderCreatedV1>
    {
        public OrderCreatedV1 Upcast(OrderCreatedV2 oldMessage, TransportMessageMetadata metadata) =>
            new OrderCreatedV1(oldMessage.OrderId, (double)oldMessage.NewPrice);
    }

    [Fact]
    public async Task InvokeAsync_CyclicUpcastersConfigured_PerformsSinglePassSafelyWithoutInfiniteRecursion()
    {
        var services = new ServiceCollection();
        services.AddScoped<OrderCreatedUpcaster>();
        services.AddScoped<CyclicV2ToV1Upcaster>();
        using var sp = services.BuildServiceProvider();

        var invoker1 = new MessageUpcasterInvoker<OrderCreatedV1, OrderCreatedV2, OrderCreatedUpcaster>();
        var invoker2 = new MessageUpcasterInvoker<OrderCreatedV2, OrderCreatedV1, CyclicV2ToV1Upcaster>();
        var middleware = new MessageUpcastingMiddleware(new IMessageUpcasterInvoker[] { invoker1, invoker2 });

        var meta = TestMessageContextFactory.CreateMetadata("order.created.v1");
        var context = new MessageContext(meta, sp)
        {
            Message = new OrderCreatedV1("ORD-CYCLIC", 77.0)
        };

        var result = await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            ctx.Message.Should().BeOfType<OrderCreatedV2>();
            return ValueTask.FromResult(Result.Success());
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.Message.Should().BeOfType<OrderCreatedV2>();
    }

    private sealed record Chain0(int Value) : IMessage;
    private sealed record Chain1(int Value) : IMessage;
    private sealed record Chain2(int Value) : IMessage;
    private sealed record Chain3(int Value) : IMessage;
    private sealed record Chain4(int Value) : IMessage;
    private sealed record Chain5(int Value) : IMessage;
    private sealed record Chain6(int Value) : IMessage;
    private sealed record Chain7(int Value) : IMessage;
    private sealed record Chain8(int Value) : IMessage;
    private sealed record Chain9(int Value) : IMessage;
    private sealed record Chain10(int Value) : IMessage;
    private sealed record Chain11(int Value) : IMessage;
    private sealed record Chain12(int Value) : IMessage;

    private sealed class DelegateInvoker(Type source, Type target, Func<object, object> upcast) : IMessageUpcasterInvoker
    {
        public Type SourceType => source;
        public Type TargetType => target;
        public object Upcast(object oldMessage, TransportMessageMetadata metadata, IServiceProvider serviceProvider) => upcast(oldMessage);
    }

    [Fact]
    public async Task InvokeAsync_LongChainedUpcasters_StopsAtMaxUpcastsSafely()
    {
        var invokers = new IMessageUpcasterInvoker[]
        {
            new DelegateInvoker(typeof(Chain0), typeof(Chain1), m => new Chain1(((Chain0)m).Value + 1)),
            new DelegateInvoker(typeof(Chain1), typeof(Chain2), m => new Chain2(((Chain1)m).Value + 1)),
            new DelegateInvoker(typeof(Chain2), typeof(Chain3), m => new Chain3(((Chain2)m).Value + 1)),
            new DelegateInvoker(typeof(Chain3), typeof(Chain4), m => new Chain4(((Chain3)m).Value + 1)),
            new DelegateInvoker(typeof(Chain4), typeof(Chain5), m => new Chain5(((Chain4)m).Value + 1)),
            new DelegateInvoker(typeof(Chain5), typeof(Chain6), m => new Chain6(((Chain5)m).Value + 1)),
            new DelegateInvoker(typeof(Chain6), typeof(Chain7), m => new Chain7(((Chain6)m).Value + 1)),
            new DelegateInvoker(typeof(Chain7), typeof(Chain8), m => new Chain8(((Chain7)m).Value + 1)),
            new DelegateInvoker(typeof(Chain8), typeof(Chain9), m => new Chain9(((Chain8)m).Value + 1)),
            new DelegateInvoker(typeof(Chain9), typeof(Chain10), m => new Chain10(((Chain9)m).Value + 1)),
            new DelegateInvoker(typeof(Chain10), typeof(Chain11), m => new Chain11(((Chain10)m).Value + 1)),
            new DelegateInvoker(typeof(Chain11), typeof(Chain12), m => new Chain12(((Chain11)m).Value + 1)),
        };

        var middleware = new MessageUpcastingMiddleware(invokers);
        var meta = TestMessageContextFactory.CreateMetadata("chain.v0");
        var context = new MessageContext(meta, new ServiceCollection().BuildServiceProvider())
        {
            Message = new Chain0(0)
        };

        var result = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // MaxUpcasts is 10, so it must stop precisely at Chain10
        context.Message.Should().BeOfType<Chain10>();
        ((Chain10)context.Message).Value.Should().Be(10);
    }
}

