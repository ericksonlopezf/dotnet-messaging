# Quick Start Guide — EricksonLopez.Messaging

Get started with `EricksonLopez.Messaging` in under 5 minutes. This guide walks you through installing packages, creating strongly-typed immutable messages, implementing functional handlers returning `ValueTask<Result>`, and publishing messages.

---

## 1. Installation

Install the required packages via NuGet:

```bash
# Core framework and Roslyn compile-time tools
dotnet add package EricksonLopez.Messaging
dotnet add package EricksonLopez.Messaging.Generators
dotnet add package EricksonLopez.Messaging.Analyzers

# Optional: OpenTelemetry observability
dotnet add package EricksonLopez.Messaging.OpenTelemetry
```

---

## 2. Define Message Contract

Messages are defined as immutable C# records implementing `IMessage` and decorated with `[MessageType("...")]`:

```csharp
using System;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;

namespace MyApp.Contracts;

[MessageType("orders.order-created.v1")]
public sealed record OrderCreatedMessage(
    Guid OrderId,
    string CustomerId,
    decimal TotalAmount,
    DateTimeOffset CreatedAtUtc) : IMessage;
```

---

## 3. Implement Message Handler (`IMessageHandler<T>`)

Handlers implement `IMessageHandler<TMessage>` and return `ValueTask<Result>` for pure functional flow control without throwing exceptions:

```csharp
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;

namespace MyApp.Handlers;

public sealed class OrderCreatedHandler : IMessageHandler<OrderCreatedMessage>
{
    private readonly ILogger<OrderCreatedHandler> _logger;

    public OrderCreatedHandler(ILogger<OrderCreatedHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<Result> HandleAsync(
        OrderCreatedMessage message,
        MessageContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Processed order: {OrderId} for customer {CustomerId} totaling {Total:C}. [MessageId: {MessageId}]",
            message.OrderId,
            message.CustomerId,
            message.TotalAmount,
            context.Metadata.MessageId);

        return ValueTask.FromResult(Result.Success());
    }
}
```

---

## 4. Configure Application Host & Pipeline

In your `Program.cs`, register messaging and configure the resiliency pipeline:

```csharp
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using EricksonLopez.Messaging.Extensions;
using MyApp.Contracts;
using MyApp.Handlers;

var builder = Host.CreateApplicationBuilder(args);

// 1. OpenTelemetry distributed tracing
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddMessagingInstrumentation();
        tracing.AddConsoleExporter();
    });

// 2. Register Messaging core and resiliency pipeline
builder.Services.AddMessaging(options =>
{
    // Fast-fail protection during downstream outages
    options.AddCircuitBreaker(cb =>
    {
        cb.FailureThreshold = 5;
        cb.BreakDuration = TimeSpan.FromSeconds(30);
    });

    // Execution time limits
    options.AddHandlerTimeout(TimeSpan.FromSeconds(15));
    options.AddUpcasting();
});

// 3. Register message handlers
builder.Services.AddMessageHandler<OrderCreatedMessage, OrderCreatedHandler>();
// Or automatically via Source Generators:
// builder.Services.AddGeneratedMessagingHandlers();

var app = builder.Build();
_ = app.RunAsync();
```

---

## 5. Publish Messages

Inject `IMessagePublisher` into your services or endpoints:

```csharp
using System;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;
using MyApp.Contracts;

public sealed class CheckoutService
{
    private readonly IMessagePublisher _publisher;

    public CheckoutService(IMessagePublisher publisher)
    {
        _publisher = publisher;
    }

    public async Task<Result> PlaceOrderAsync(Guid orderId, string customerId, decimal amount)
    {
        var message = new OrderCreatedMessage(
            OrderId: orderId,
            CustomerId: customerId,
            TotalAmount: amount,
            CreatedAtUtc: DateTimeOffset.UtcNow);

        var publishOptions = new MessagePublishOptions
        {
            Destination = "orders.topic",
            PartitionKey = customerId
        };

        var result = await _publisher.PublishAsync(message, publishOptions);

        if (result.IsFailure)
        {
            // Handle publish error gracefully via Result pattern
            return Result.Failure(result.Error);
        }

        return Result.Success();
    }
}
```

---

## Next Steps

- Explore the **[Technical Cookbook](cookbook.md)** for broker integration recipes (RabbitMQ, Azure Service Bus, AWS SQS, Kafka).
- Read the **[Architecture Blueprint](architecture.md)** to understand internal design and boundaries.
- Consult the **[Public API Reference](public-api-reference.md)** for detailed type documentation.
