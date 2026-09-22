// Copyright © Erickson Lopez. MIT License.
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Dispatch;
using EricksonLopez.Messaging.Middleware;
using EricksonLopez.Messaging.Serialization;
using EricksonLopez.Messaging.Transport.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace EricksonLopez.Messaging.Benchmarks;

using Result = EricksonLopez.Result.Result;
using Error = EricksonLopez.Result.Error;

/// <summary>
/// Provides benchmark scenarios measuring dispatch, serialization, publishing, and pipeline throughput.
/// </summary>
[MemoryDiagnoser]
public class MessagingBenchmarks : IDisposable
{
    /// <summary>
    /// Represents a benchmark order created message.
    /// </summary>
    /// <param name="OrderId">The unique identifier of the order.</param>
    /// <param name="Total">The total monetary amount of the order.</param>
    [MessageType("benchmarks.order-created.v1")]
    public sealed record BenchmarkOrderCreated(Guid OrderId, decimal Total) : IMessage;

    /// <summary>
    /// Represents a benchmark payment processed message.
    /// </summary>
    /// <param name="PaymentId">The unique identifier of the payment.</param>
    /// <param name="Amount">The processed payment amount.</param>
    [MessageType("benchmarks.payment-processed.v1")]
    public sealed record BenchmarkPaymentProcessed(Guid PaymentId, decimal Amount) : IMessage;

    /// <summary>
    /// Provides a benchmark message handler for <see cref="BenchmarkOrderCreated"/>.
    /// </summary>
    public sealed class BenchmarkOrderCreatedHandler : IMessageHandler<BenchmarkOrderCreated>
    {
        /// <inheritdoc />
        public ValueTask<Result> HandleAsync(
            BenchmarkOrderCreated message,
            MessageContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success());
        }
    }

    /// <summary>
    /// Provides a benchmark message handler for <see cref="BenchmarkPaymentProcessed"/>.
    /// </summary>
    public sealed class BenchmarkPaymentProcessedHandler : IMessageHandler<BenchmarkPaymentProcessed>
    {
        /// <inheritdoc />
        public ValueTask<Result> HandleAsync(
            BenchmarkPaymentProcessed message,
            MessageContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success());
        }
    }

    /// <summary>
    /// Provides a benchmark message handler that returns a functional validation failure.
    /// </summary>
    public sealed class FailingHandler : IMessageHandler<BenchmarkOrderCreated>
    {
        /// <inheritdoc />
        public ValueTask<Result> HandleAsync(
            BenchmarkOrderCreated message,
            MessageContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Failure(Error.Validation("Validation.Failed", "Validation error.")));
        }
    }

    private IServiceProvider _serviceProvider = null!;
    private IMessageSerializer _serializer = null!;
    private DefaultMessageDispatcher _dispatcher = null!;
    private DefaultMessageDispatcher _dispatcherWithPipeline = null!;
    private MessagePublisher _publisher = null!;
    private ReadOnlyMemory<byte> _payload;
    private TransportMessageMetadata _metadata = null!;
    private BenchmarkOrderCreated _sampleMessage = null!;
    private CancellationTokenSource _cancelledCts = null!;
    private bool _disposed;

    /// <summary>
    /// Initializes benchmark dependencies, services, and pre-allocated message payloads.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddScoped<BenchmarkOrderCreatedHandler>();
        services.AddScoped<BenchmarkPaymentProcessedHandler>();
        services.AddScoped<FailingHandler>();
        _serviceProvider = services.BuildServiceProvider();

        _serializer = new NativeAotJsonSerializer();
        _dispatcher = new DefaultMessageDispatcher(_serializer);
        _dispatcher.RegisterHandler<BenchmarkOrderCreated, BenchmarkOrderCreatedHandler>("benchmarks.order-created.v1");
        _dispatcher.RegisterHandler<BenchmarkPaymentProcessed, BenchmarkPaymentProcessedHandler>("benchmarks.payment-processed.v1");

        // Dispatcher with middlewares
        var middlewares = new IMessageMiddleware[]
        {
            new TracingMiddleware(),
            new LoggingMiddleware(),
            new ExceptionHandlingMiddleware()
        };
        _dispatcherWithPipeline = new DefaultMessageDispatcher(_serializer, middlewares: middlewares);
        _dispatcherWithPipeline.RegisterHandler<BenchmarkOrderCreated, BenchmarkOrderCreatedHandler>("benchmarks.order-created.v1");

        var transport = new InMemoryMessageTransport();
        _publisher = new MessagePublisher(transport, _serializer);

        _sampleMessage = new BenchmarkOrderCreated(Guid.NewGuid(), 199.99m);
        _payload = _serializer.Serialize(_sampleMessage);
        _metadata = TransportMessageMetadata.Create("benchmarks.order-created.v1");

        _cancelledCts = new CancellationTokenSource();
        _cancelledCts.Cancel();
    }

    /// <summary>
    /// Cleans up benchmark resources after test completion.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    /// <summary>
    /// Releases the resources used by this instance.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancelledCts?.Dispose();
    }

    /// <summary>
    /// Measures baseline dispatch latency without middleware.
    /// </summary>
    /// <returns>A value task representing the asynchronous operation.</returns>
    [Benchmark(Baseline = true)]
    public async ValueTask<Result> Dispatch_Simple()
    {
        using var scope = _serviceProvider.CreateScope();
        return await _dispatcher.DispatchAsync(
            "benchmarks.order-created.v1",
            _payload,
            _metadata,
            scope.ServiceProvider,
            CancellationToken.None);
    }

    /// <summary>
    /// Measures dispatch throughput including tracing, logging, and exception handling middleware.
    /// </summary>
    /// <returns>A value task representing the asynchronous operation.</returns>
    [Benchmark]
    public async ValueTask<Result> Dispatch_WithDefaultPipeline()
    {
        using var scope = _serviceProvider.CreateScope();
        return await _dispatcherWithPipeline.DispatchAsync(
            "benchmarks.order-created.v1",
            _payload,
            _metadata,
            scope.ServiceProvider,
            CancellationToken.None);
    }

    /// <summary>
    /// Measures dispatch latency when the cancellation token is pre-cancelled.
    /// </summary>
    /// <returns>A value task representing the asynchronous operation.</returns>
    [Benchmark]
    public async ValueTask<Result> Dispatch_CancelledPath()
    {
        using var scope = _serviceProvider.CreateScope();
        try
        {
            return await _dispatcher.DispatchAsync(
                "benchmarks.order-created.v1",
                _payload,
                _metadata,
                scope.ServiceProvider,
                _cancelledCts.Token);
        }
        catch (OperationCanceledException)
        {
            return Result.Success();
        }
    }

    /// <summary>
    /// Measures message publish latency via the in-memory transport.
    /// </summary>
    /// <returns>A value task representing the asynchronous operation.</returns>
    [Benchmark]
    public async ValueTask<Result> Publish_Message()
    {
        return await _publisher.PublishAsync(_sampleMessage);
    }

    /// <summary>
    /// Measures point-to-point send latency via the in-memory transport.
    /// </summary>
    /// <returns>A value task representing the asynchronous operation.</returns>
    [Benchmark]
    public async ValueTask<Result> Send_Message()
    {
        return await _publisher.SendAsync(_sampleMessage, "order-queue");
    }

    /// <summary>
    /// Measures payload serialization performance into byte memory.
    /// </summary>
    /// <returns>The serialized byte payload memory.</returns>
    [Benchmark]
    public ReadOnlyMemory<byte> Serialization_Serialize()
    {
        return _serializer.Serialize(_sampleMessage);
    }

    /// <summary>
    /// Measures payload deserialization performance from byte memory into a strongly-typed instance.
    /// </summary>
    /// <returns>The deserialized message instance.</returns>
    [Benchmark]
    public BenchmarkOrderCreated Deserialization_Deserialize()
    {
        return _serializer.Deserialize<BenchmarkOrderCreated>(_payload);
    }

    /// <summary>
    /// Measures message type name resolution performance via reflection attribute caching.
    /// </summary>
    /// <returns>The resolved type name string.</returns>
    [Benchmark]
    public string Handler_ResolveTypeName()
    {
        var attr = _sampleMessage.GetType().GetCustomAttribute<MessageTypeAttribute>();
        return attr?.TypeName ?? _sampleMessage.GetType().Name;
    }
}
