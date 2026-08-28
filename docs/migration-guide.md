# Migration Guide — EricksonLopez.Messaging

This guide covers breaking changes, renamed APIs, and recommended migration paths between versions.

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
