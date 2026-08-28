# Level 01: Event Bus & Broker Transports

## 1. Publishing and Subscribing
`EricksonLopez.Messaging` decouples message definitions from underlying transport infrastructure:

```csharp
using EricksonLopez.Messaging;
using EricksonLopez.Messaging.Abstractions;

public sealed record OrderCreatedEvent(Guid OrderId, decimal Amount, DateTime CreatedAt) : IEvent;

public sealed class OrderCreatedConsumer : IConsumer<OrderCreatedEvent>
{
    public async ValueTask ConsumeAsync(ConsumeContext<OrderCreatedEvent> context, CancellationToken ct)
    {
        var message = context.Message;
        // Process order asynchronously
    }
}
```

---

## 2. Pluggable Transport Configuration
Configure transports seamlessly across RabbitMQ, Kafka, Azure Service Bus, and AWS SQS:

```csharp
// Configure RabbitMQ Transport
services.AddMessaging(options =>
{
    options.UseRabbitMQ(cfg =>
    {
        cfg.Host = "amqp://localhost:5672";
        cfg.ExchangeName = "domain.events";
    });
    options.AddConsumer<OrderCreatedConsumer>();
});
```
