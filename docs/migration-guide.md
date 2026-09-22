# Migration Guide — EricksonLopez.Messaging

This guide covers breaking changes, renamed APIs, and recommended migration paths between versions.

---

## Migrating from 1.0.0 to 2.0.0 (Released 2026-09-22)

Version 2.0.0 introduces enterprise message deduplication, dynamic partition routing, and hardening across all transport drivers. It resolves critical edge cases in consumer failure handling, sliding circuit breakers, and Native AOT trim safety.

### 1. Public Interface Changes
- **`IMessageUpcasterInvoker`**: Added `Type TargetType { get; }`. If you authored a custom implementation of `IMessageUpcasterInvoker`, implement `TargetType => typeof(TDestination)` or inherit from `MessageUpcasterInvoker<TOld, TNew, TUpcaster>`.

### 2. Constructor Signature Binary Changes
The following public constructors had optional parameters added:
- **`DefaultMessageDispatcher`**: Parameter `bindings` changed from `IDictionary<string, HandlerBinding>?` to `IDictionary<string, IReadOnlyList<HandlerBinding>>?` to support multi-handler Pub/Sub dispatch. Parameter `upcasters` was added.
- **`RetryMiddleware`**: Replaced 3-parameter constructor with a 5-parameter constructor accepting `maxDelay` and `shouldRetry`. Use `new RetryMiddleware(new RetryOptions { ... })` or recompile.
- **`MessagePublisher`**: Added `IEnumerable<IPartitionKeyResolver>?`. Recompile or resolve via `IServiceProvider`.
- **`MessageConsumer`**: Added `IOptions<MessageConsumerOptions>?`. Recompile or resolve via `IServiceProvider`.

### 3. Native AOT & Serialization Fallback Removal
- **`NativeAotJsonSerializer`**: Removed `DefaultJsonTypeInfoResolver` reflection fallback to guarantee Native AOT trim-safety. All message contracts must be registered with a source-generated `JsonSerializerContext` (or use `EricksonLopez.Messaging.Generators`). Unregistered POCOs will throw `NotSupportedException`.
- **`EricksonLopez.Messaging.Kafka`**: Configured `<IsAotCompatible>false</IsAotCompatible>`. Kafka transport cannot be compiled with `<PublishAot>true</PublishAot>` due to `librdkafka` C-interop constraints.

### 4. Transport & Concurrency Behavior
- **`MessageConsumer` Fault Handling**: Failed message execution now routes raw payloads to `IDeadLetterQueue` (returning `TransportAckResult.DeadLetter`) instead of silently acknowledging (`TransportAckResult.Ack`). Shutdown cancellations now return `TransportAckResult.NackRequeue`.
- **`AwsSqsMessageTransport`**: Messages received in each poll batch execute concurrently up to `MaxConcurrency` via `Parallel.ForEachAsync`.
- **`InMemoryMessageTransport`**: Messages with a `PartitionKey` are partitioned into dedicated bounded channels to guarantee strict serial ordering per key.

---

## Migrating to 1.0.0

Version 1.0.0 is the inaugural stable release. Projects referencing pre-release packages should perform the following migrations.

---

### 1. DI Extension Method Renames

Transport-specific DI extension methods have been renamed to include the `Messaging` infix for consistency:

| Before (pre-release) | After (1.0.0) |
|---|---|
| `AddRabbitMqMessaging()` | `AddRabbitMqMessagingTransport()` |
| `AddAzureServiceBusMessaging()` | `AddAzureServiceBusMessagingTransport()` |
| `AddKafkaMessaging()` | `AddKafkaMessagingTransport()` |
| `AddAwsSqsMessaging()` | `AddAwsSqsMessagingTransport()` |

**Migration action**: Rename all transport registration calls in `Program.cs`.

---

### 2. Transport Options — Removed Properties

The following options properties were removed in 1.0.0 as they were not implemented by the drivers:

#### `RabbitMqTransportOptions`

| Removed | Replacement |
|---|---|
| `PublisherConfirms` | Not implemented in this release. Use broker-level configuration. |
| `ExchangeType` | Not implemented. Uses direct exchange by default. |
| `Durable` | Not implemented. All exchanges/queues created as durable. |
| `AutoDelete` | Not implemented. |
| `Mandatory` | Not implemented. |

#### `AzureServiceBusTransportOptions`

| Removed | Replacement |
|---|---|
| `QueueOrTopicName` | Set `Destination` in `MessagePublishOptions` per publish call. |
| `SubscriptionName` | Set via subscriber side configuration. |
| `MaxConcurrentCalls` | Set `MaxConcurrency` in `TransportSubscriptionOptions` per subscription. |
| `PrefetchCount` | Set `PrefetchCount` in `TransportSubscriptionOptions` per subscription. |

#### `AwsSqsTransportOptions`

| Removed | Replacement |
|---|---|
| `QueueUrl` | Not used; queue URL is derived from `Region` and message destination. |
| `AccessKey` | Use environment variables or IAM instance roles. |
| `SecretKey` | Use environment variables or IAM instance roles. |
| `VisibilityTimeoutSeconds` | Configured at the SQS queue level, not in the transport options. |

#### `KafkaTransportOptions`

| Removed | Replacement |
|---|---|
| `DefaultTopic` | Set `Destination` in `MessagePublishOptions` per publish call. |
| `AutoOffsetReset` | Not configurable at the transport level in 1.0.0. |
| `SaslMechanism` | Not configurable at the transport level in 1.0.0. |
| `SecurityProtocol` | Not configurable at the transport level in 1.0.0. |

---

### 3. `MessagingEventsOptions` — Removed Property

| Removed | Replacement |
|---|---|
| `DefaultDestination` | Use `DestinationResolver = eventType => "your.destination"` |

**Before**:
```csharp
builder.Services.AddMessagingEventPublisher(options =>
{
    options.DefaultDestination = "domain-events.topic";
});
```

**After**:
```csharp
builder.Services.AddMessagingEventPublisher(options =>
{
    options.DestinationResolver = eventType =>
        $"{eventType.Name.ToLowerInvariant()}.events.v1";
});
```

---

### 4. Handler Return Type Enforcement (`ELMSG010`)

Handlers must return `ValueTask<Result>`. If you have handlers returning `Task<Result>` or `void`, update them:

**Before**:
```csharp
public Task<Result> HandleAsync(MyMessage msg, MessageContext ctx, CancellationToken ct)
    => Task.FromResult(Result.Success());
```

**After**:
```csharp
public ValueTask<Result> HandleAsync(MyMessage msg, MessageContext ctx, CancellationToken ct)
    => ValueTask.FromResult(Result.Success());
```

---

### 5. Upcaster Interface Signature

`IMessageUpcaster<TOld, TNew>` signature is synchronous — the `Upcast` method returns `TNew`, not `Task<TNew>`:

```csharp
public sealed class OrderV1ToV2Upcaster : IMessageUpcaster<OrderPlacedV1, OrderPlacedV2>
{
    // Synchronous — no async needed
    public OrderPlacedV2 Upcast(OrderPlacedV1 old, TransportMessageMetadata metadata)
        => new(old.OrderId, old.Amount, "USD");
}
```

---

## Future Versions

Breaking changes will be documented here with each release. The project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
- Minor (1.x.0): Backwards-compatible new features.
- Patch (1.0.x): Bug fixes.
- Major (2.0.0): Breaking API changes with migration instructions in this guide.
