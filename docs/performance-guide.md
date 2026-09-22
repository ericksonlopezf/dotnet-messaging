# Performance & Optimization Guide — EricksonLopez.Messaging

This guide describes the architectural patterns and low-level engineering optimizations implemented in `EricksonLopez.Messaging` to achieve maximum throughput and minimal-allocation execution paths on the message dispatch hot path.

---

## 1. Native AOT & Zero-Reflection Execution

Traditional messaging frameworks rely on `Type.GetType()`, `MethodInfo.Invoke()`, and reflection-based serializers at runtime. This causes:
- JIT compilation pauses and cold-start latency.
- Reflection metadata overhead on the dispatch hot path.
- Incompatibility with Native AOT trimming and single-file publishing.

### How `EricksonLopez.Messaging` Solves This:
1. **Incremental Source Generators (`MessagingIncrementalGenerator`)**: Discovers all `IMessageHandler<T>` implementations at compile time and emits:
   - Strongly-typed invocation delegates `(sp, msg, ctx, ct) => handler.HandleAsync(...)`.
   - Dedicated `JsonSerializerContext` metadata resolvers for `System.Text.Json`.
2. **`MessageTypeCache`**: Resolves `[MessageType]` attribute strings into immutable static caches once during type initialization.

---

## 2. Memory Optimization & Minimal-Allocation Hot Paths

### `ValueTask<Result>` on Dispatch Paths
Unlike `Task<Result>`, `ValueTask<Result>` is a value type struct. When a message handler or middleware completes synchronously (e.g. cached validations or immediate ack), **no object is allocated on the managed heap**.

### `ReadOnlyMemory<byte>` and Zero-Copy Buffers
Serialization and transport APIs accept `ReadOnlyMemory<byte>` rather than allocating new `byte[]` arrays on every step, enabling buffer slicing and pooling without GC pressure.

---

## 3. High-Throughput Batch Publishing

Publishing messages individually generates a network roundtrip per message. For high-volume producers:

```csharp
// Always use PublishBatchAsync instead of looping PublishAsync
await publisher.PublishBatchAsync(messages);
```

Drivers supporting `IBatchMessageTransport` (such as Azure Service Bus via `ServiceBusMessageBatch` and InMemory) pack messages into unified frames, significantly multiplying throughput. For other transports (RabbitMQ, Kafka, AWS SQS), `PublishBatchAsync` executes an optimized sequential publish fallback with unified cancellation.

---

## 4. ThreadPool Starvation Prevention (`ELMSG008`)

Synchronous blocking via `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` inside message handlers blocks ThreadPool worker threads, leading to severe latency spikes and pool starvation under load.

- Roslyn Analyzer **`ELMSG008`** rejects synchronous blocking calls at compile time.
- Always use `await` and forward the `cancellationToken`.

---

## 5. In-Memory Transport Backpressure Tuning

To prevent unbounded memory consumption when producer throughput exceeds consumer capacity:

```csharp
// InMemoryTransportOptions is configured directly at service registration time.
// Use when tuning the in-memory transport for high-throughput scenarios.
var transportOptions = new InMemoryTransportOptions
{
    ChannelCapacity = 50_000,
    FullMode        = BoundedChannelFullMode.Wait // Applies non-blocking backpressure
};

// Register by adding a singleton before AddMessaging() if using custom options,
// or access via app.Services.GetRequiredService<InMemoryTransportOptions>() after construction.
Console.WriteLine($"ChannelCapacity: {transportOptions.ChannelCapacity}");
Console.WriteLine($"FullMode       : {transportOptions.FullMode}");
```
