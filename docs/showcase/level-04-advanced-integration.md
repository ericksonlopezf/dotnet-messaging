# Level 04: Advanced Integration Patterns

## Overview
Level 04 demonstrates how `EricksonLopez.Messaging` integrates with domain-driven architectures, external event publishing contracts, partitioned messaging topologies, and raw dispatcher infrastructure.

---

## 1. Domain Event Bridge (`MessagingEventPublisher`)

When building clean architecture solutions with `EricksonLopez.Events.Contracts`, in-process domain events can be seamlessly relayed across distributed message brokers using `EricksonLopez.Messaging.Events`:

```csharp
builder.Services.AddMessaging(...);
builder.Services.AddMessagingEventPublisher(options =>
{
    options.ThrowOnFailure = false;
    options.DestinationResolver = type => $"{type.Name.ToLowerInvariant()}.v1";
});
```

- **`MessagingEventsOptions.ThrowOnFailure`**: Controls whether failed message publications throw exceptions or log structured errors.
- **`MessagingEventsOptions.DestinationResolver`**: Computes the topic or queue destination dynamically from the event CLR type.

---

## 2. Partition Key Routing (`[PartitionKeyAttribute]`)

For distributed stream brokers (Apache Kafka, Azure Service Bus Sessions, AWS SQS FIFO), strict in-order processing is achieved by decorating partitioning properties with `[PartitionKey]`:

```csharp
[MessageType("orders.partitioned.v1")]
public sealed record PartitionedOrderMessage(
    Guid OrderId,
    [property: PartitionKey] string Region) : IMessage;
```

When publishing, the partition key is attached directly to the message routing metadata:

```csharp
var options = new MessagePublishOptions
{
    Destination = "orders.partitioned.topic",
    PartitionKey = message.Region
};

await publisher.PublishAsync(message, options, cancellationToken);
```

---

## 3. Direct Low-Level Dispatch (`IMessageDispatcher`)

While `IMessageConsumer` manages subscription loops automatically, infrastructure adapters (e.g., custom webhook endpoints or legacy protocol gateways) can invoke `IMessageDispatcher` directly:

```csharp
var dispatcher = serviceProvider.GetRequiredService<IMessageDispatcher>();
var serializer = serviceProvider.GetRequiredService<IMessageSerializer>();

ReadOnlyMemory<byte> payload = serializer.Serialize(ping);
var metadata = TransportMessageMetadata.Create("ping.v1", correlationId: "corr-001");

ValueTask<Result> dispatchResult = await dispatcher.DispatchAsync(
    messageType: "ping.v1",
    payload: payload,
    metadata: metadata,
    serviceProvider: serviceScope.ServiceProvider,
    cancellationToken: cancellationToken);
```
