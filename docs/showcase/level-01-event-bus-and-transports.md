# Level 01: Event Bus & Broker Transports

## 1. Defining Messages and Handlers

`EricksonLopez.Messaging` decouples message contracts and handler business logic from underlying transport infrastructure.

Messages are immutable records implementing `IMessage` decorated with the `[MessageType]` attribute:

```csharp
using System;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;

[MessageType("orders.order-created.v1")]
public sealed record OrderCreatedEvent(
    Guid OrderId,
    decimal Amount,
    DateTimeOffset CreatedAt) : IMessage;
```

Handlers implement `IMessageHandler<TMessage>` and return `ValueTask<Result>` for deterministic, zero-exception control flow:

```csharp
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;

public sealed class OrderCreatedHandler : IMessageHandler<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedHandler> _logger;

    public OrderCreatedHandler(ILogger<OrderCreatedHandler> logger) => _logger = logger;

    public ValueTask<Result> HandleAsync(
        OrderCreatedEvent message,
        MessageContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing order {OrderId} totaling {Amount:C}", message.OrderId, message.Amount);
        return ValueTask.FromResult(Result.Success());
    }
}
```

---

## 2. Pluggable Transport Configuration

The transport layer is swappable via DI without changing handler or publisher code:

### In-Memory Transport (Local development & testing)

```csharp
builder.Services.AddMessaging();
builder.Services.AddMessageHandler<OrderCreatedEvent, OrderCreatedHandler>();
```

### RabbitMQ Transport (AMQP 0-9-1)

```csharp
builder.Services.AddMessaging();
builder.Services.AddRabbitMqMessagingTransport(options =>
{
    options.HostName = "localhost";
    options.Port = 5672;
    options.UserName = "guest";
    options.Password = "guest";
    options.VirtualHost = "/";
});
builder.Services.AddMessageHandler<OrderCreatedEvent, OrderCreatedHandler>();
```

### Azure Service Bus Transport

```csharp
builder.Services.AddMessaging();
builder.Services.AddAzureServiceBusMessagingTransport(options =>
{
    options.ConnectionString = "Endpoint=sb://your-namespace.servicebus.windows.net/...";
    // Or Managed Identity:
    // options.FullyQualifiedNamespace = "your-namespace.servicebus.windows.net";
    // options.TokenCredential = new DefaultAzureCredential();
});
builder.Services.AddMessageHandler<OrderCreatedEvent, OrderCreatedHandler>();
```

### Publishing Messages

```csharp
var publisher = app.Services.GetRequiredService<IMessagePublisher>();
var result = await publisher.PublishAsync(new OrderCreatedEvent(Guid.NewGuid(), 149.99m, DateTimeOffset.UtcNow));

if (result.IsFailure)
{
    // Handle transient or permanent failure functionally
    logger.LogError("Failed to publish: {Error}", result.Error.Description);
}
```
