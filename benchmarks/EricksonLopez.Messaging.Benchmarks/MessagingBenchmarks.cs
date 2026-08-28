// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Dispatch;
using EricksonLopez.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace EricksonLopez.Messaging.Benchmarks;

[MemoryDiagnoser]
public class MessagingBenchmarks
{
    [MessageType("benchmarks.order-created.v1")]
    public sealed record BenchmarkOrderCreated(Guid OrderId, decimal Total) : IMessage;

    public sealed class BenchmarkOrderCreatedHandler : IMessageHandler<BenchmarkOrderCreated>
    {
        public ValueTask<EricksonLopez.Result.Result> HandleAsync(
            BenchmarkOrderCreated message,
            MessageContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(EricksonLopez.Result.Result.Success());
        }
    }

    private IServiceProvider _serviceProvider = null!;
    private IMessageSerializer _serializer = null!;
    private DefaultMessageDispatcher _dispatcher = null!;
    private ReadOnlyMemory<byte> _payload;
    private TransportMessageMetadata _metadata = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddScoped<BenchmarkOrderCreatedHandler>();
        _serviceProvider = services.BuildServiceProvider();

        _serializer = new NativeAotJsonSerializer();
        _dispatcher = new DefaultMessageDispatcher(_serializer);
        _dispatcher.RegisterHandler<BenchmarkOrderCreated, BenchmarkOrderCreatedHandler>("benchmarks.order-created.v1");

        var msg = new BenchmarkOrderCreated(Guid.NewGuid(), 199.99m);
        _payload = _serializer.Serialize(msg);
        _metadata = TransportMessageMetadata.Create("benchmarks.order-created.v1");
    }

    [Benchmark(Baseline = true)]
    public async ValueTask<EricksonLopez.Result.Result> DispatchMessageBenchmark()
    {
        using var scope = _serviceProvider.CreateScope();
        return await _dispatcher.DispatchAsync(
            "benchmarks.order-created.v1",
            _payload,
            _metadata,
            scope.ServiceProvider,
            CancellationToken.None);
    }
}
