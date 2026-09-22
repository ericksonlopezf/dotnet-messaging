# Level 05: Background Processing & Hosting Lifecycle

## Overview
Level 05 demonstrates how the messaging consumer integrates into the ASP.NET Core / Microsoft.Extensions.Hosting generic host lifecycle, handling background dispatch, in-flight message draining, and graceful shutdown without message loss.

---

## 1. Asynchronous Hosted Consumer (`MessagingConsumerHostedService`)

When calling `builder.Services.AddMessaging(...)`, the framework automatically registers `MessagingConsumerHostedService` as an `IHostedService`:

- **Host Startup**: Invokes `IMessageConsumer.StartAsync(cancellationToken)` to begin listening for incoming messages on registered transport destinations.
- **Host Shutdown**: Executes a two-phase graceful shutdown:
  1. `IMessageConsumer.StopReceivingAsync(cancellationToken)`: Halts ingestion of new messages from the broker.
  2. `IMessageConsumer.DrainInFlightMessagesAsync(cancellationToken)`: Awaits all currently active handlers to complete processing before disposing resources.

---

## 2. Programmatic Lifecycle Control

In specialized hosting environments (e.g., worker pools or dynamic scaling controllers), consumers can be controlled programmatically:

```csharp
var consumer = app.Services.GetRequiredService<IMessageConsumer>();

// 1. Stop accepting new incoming messages from broker
await consumer.StopReceivingAsync(cancellationToken);

// 2. Wait for active handlers to drain safely
await consumer.DrainInFlightMessagesAsync(cancellationToken);

// 3. Graceful shutdown finished
```

---

## 3. Dynamic Destination Registration

Destinations can be added dynamically prior to consumer startup:

```csharp
var consumer = app.Services.GetRequiredService<IMessageConsumer>();

if (consumer is MessageConsumer concreteConsumer)
{
    concreteConsumer.AddDestination("orders.priority.v1");
}
```
