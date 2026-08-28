# Getting Started — EricksonLopez.Messaging

A step-by-step guide to integrate `EricksonLopez.Messaging` into a .NET 10 application from scratch.

---

## Prerequisites

- .NET 10.0 SDK
- A .NET 10 project (`dotnet new worker` recommended for background processing)

---

## Step 1: Create the Project

```bash
dotnet new worker -n MyMessagingService
cd MyMessagingService
```

---

## Step 2: Install NuGet Packages

```bash
dotnet add package EricksonLopez.Messaging
dotnet add package EricksonLopez.Messaging.Generators
dotnet add package EricksonLopez.Messaging.Analyzers
dotnet add package EricksonLopez.Messaging.OpenTelemetry
```

For a broker transport, add one of:

```bash
dotnet add package EricksonLopez.Messaging.RabbitMQ
dotnet add package EricksonLopez.Messaging.AzureServiceBus
dotnet add package EricksonLopez.Messaging.Kafka
dotnet add package EricksonLopez.Messaging.AwsSqs
```

---

## Step 3: Define a Message Contract

```csharp
using System;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;

[MessageType("orders.order-created.v1")]
public sealed record OrderCreatedMessage(
    Guid   OrderId,
    string CustomerId,
    decimal TotalAmount) : IMessage;
```

---

## Step 4: Implement a Message Handler

```csharp
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;

public sealed class OrderCreatedHandler : IMessageHandler<OrderCreatedMessage>
{
    private readonly ILogger<OrderCreatedHandler> _logger;
    public OrderCreatedHandler(ILogger<OrderCreatedHandler> logger) => _logger = logger;

    public ValueTask<Result> HandleAsync(
        OrderCreatedMessage message,
        MessageContext      context,
        CancellationToken   cancellationToken = default)
    {
        _logger.LogInformation(
            "Processing order {OrderId} for {CustomerId}. [MessageId: {MessageId}]",
            message.OrderId, message.CustomerId, context.Metadata.MessageId);
        return ValueTask.FromResult(Result.Success());
    }
}
```

---

## Step 5: Configure the Host

```csharp
using System;
using EricksonLopez.Messaging.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddMessagingInstrumentation().AddConsoleExporter());

builder.Services.AddMessaging(options =>
{
    options.AddCircuitBreaker(cb =>
    {
        cb.FailureThreshold = 5;
        cb.BreakDuration    = TimeSpan.FromSeconds(30);
    });
    options.AddHandlerTimeout(TimeSpan.FromSeconds(15));
    options.AddRetry(retry =>
    {
        retry.MaxRetries   = 3;
        retry.InitialDelay = TimeSpan.FromMilliseconds(200);
    });
    options.AddUpcasting();
});

builder.Services.AddMessageHandler<OrderCreatedMessage, OrderCreatedHandler>();
builder.Services.AddMessagingHealthCheck();

await builder.Build().RunAsync();
```

---

## Step 6: Publish a Message

```csharp
var publisher = app.Services.GetRequiredService<IMessagePublisher>();
var result = await publisher.PublishAsync(
    new OrderCreatedMessage(Guid.NewGuid(), "CUST-001", 149.99m),
    new MessagePublishOptions { Destination = "orders.order-created.v1" });
```

---

## Next Steps

| Goal | Guide |
|---|---|
| Production broker integration | [Cookbook](cookbook.md) (Recipes 6-9) |
| Architecture internals | [Architecture Blueprint](architecture.md) |
| Full API reference | [Public API Reference](public-api-reference.md) |
| Best practices | [Best Practices](best-practices.md) |
| Integration testing | [Cookbook](cookbook.md) (Recipe 12) |
| Performance tuning | [Performance Guide](performance-guide.md) |
