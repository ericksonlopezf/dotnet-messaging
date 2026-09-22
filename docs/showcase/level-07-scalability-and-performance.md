# Level 07: Scalability & High-Throughput Tuning

## Overview
Level 07 demonstrates how to configure worker concurrency, channel backpressure, prefetch limits, batch transports, and OpenTelemetry performance instrumentation.

---

## 1. Concurrency & Prefetch Tuning (`TransportSubscriptionOptions`)

Subscription processing parameters are configurable per destination or globally:

```csharp
var subscriptionOptions = new TransportSubscriptionOptions
{
    MaxConcurrency = Environment.ProcessorCount * 4,
    PrefetchCount = 50,
    ConsumerGroup = "payment-processors"
};
```

- **`MaxConcurrency`**: Bounds maximum concurrent handler executions to prevent thread pool exhaustion.
- **`PrefetchCount`**: Configures local message pre-buffering to hide network roundtrip latency.
- **`ConsumerGroup`**: Defines competing consumer cluster partitions on brokers like Kafka or Azure Service Bus.

---

## 2. Channel Backpressure Tuning (`InMemoryTransportOptions`)

For in-memory architectures, channel capacity bounds prevent unbounded memory growth under sudden traffic spikes:

```csharp
var inMemoryOptions = new InMemoryTransportOptions
{
    ChannelCapacity = 50_000,
    FullMode = BoundedChannelFullMode.Wait
};
```

---

## 3. High-Throughput Batch Publishing (`IBatchMessageTransport`)

Transports implementing `IBatchMessageTransport` (such as `AzureServiceBusMessageTransport`) pack entire message collections into unified network payloads:

```csharp
ValueTask<Result> PublishBatchRawAsync(
    string destination,
    IReadOnlyList<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)> batch,
    CancellationToken cancellationToken);
```

On drivers without batch support, `MessagePublisher` transparently falls back to sequential single-message publish while preserving unified cancellation.

---

## 4. OpenTelemetry Metrics & Diagnostics

All publishing and consumption operations emit standardized metrics via `MessagingDiagnostics`:
- `MessagingDiagnostics.ActivitySourceName`: Semantic tracing activities.
- `MessagingDiagnostics.MeterName`: High-precision meters tracking publish latency, dispatch duration, and active handler counts.
