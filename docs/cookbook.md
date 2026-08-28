# Technical Cookbook & Integration Recipes — EricksonLopez.Messaging

Collection of verified design and integration recipes for real-world scenarios using `EricksonLopez.Messaging`.

---

## Recipe Index

- [Recipe 1: Minimal Setup with Generic Host](#recipe-1-minimal-setup-with-generic-host)
- [Recipe 2: Resiliency with Circuit Breaker and Handler Timeouts](#recipe-2-resiliency-with-circuit-breaker-and-handler-timeouts)
- [Recipe 3: High-Throughput Batch Publishing](#recipe-3-high-throughput-batch-publishing)
- [Recipe 4: Schema Evolution and Message Upcasting](#recipe-4-schema-evolution-and-message-upcasting)
- [Recipe 5: Domain Events Bridge with EricksonLopez.Messaging.Events](#recipe-5-domain-events-bridge-with-ericksonlopezmessagingevents)
- [Recipe 6: Azure Service Bus with Managed Identity (DefaultAzureCredential)](#recipe-6-azure-service-bus-with-managed-identity-defaultazurecredential)
- [Recipe 7: RabbitMQ with QoS Prefetch and Publisher Confirms](#recipe-7-rabbitmq-with-qos-prefetch-and-publisher-confirms)
- [Recipe 8: Apache Kafka with Partition Key Routing](#recipe-8-apache-kafka-with-partition-key-routing)
- [Recipe 9: AWS SQS with Long Polling and Delayed Deferral](#recipe-9-aws-sqs-with-long-polling-and-delayed-deferral)
- [Recipe 10: Custom Context & Security Middleware](#recipe-10-custom-context--security-middleware)
- [Recipe 11: Distributed Tracing with OpenTelemetry](#recipe-11-distributed-tracing-with-opentelemetry)
- [Recipe 12: In-Memory Integration Testing with InMemoryTestHarness](#recipe-12-in-memory-integration-testing-with-inmemorytestharness)

---

## Recipe 1: Minimal Setup with Generic Host

### Problem
You need to set up an asynchronous worker or microservice to process messages in-memory or from a broker with minimal memory overhead and fast startup.

### Solution
Use `builder.Services.AddMessaging()` and register strongly-typed handlers with `AddMessageHandler<TMessage, THandler>()`.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Extensions;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Register messaging and handlers
builder.Services.AddMessaging();
builder.Services.AddMessageHandler<PingMessage, PingHandler>();

var app = builder.Build();
_ = app.RunAsync();

var publisher = app.Services.GetRequiredService<IMessagePublisher>();
await publisher.PublishAsync(new PingMessage("PING-123"));

await Task.Delay(500);
await app.StopAsync();

[MessageType("diagnostics.ping.v1")]
public sealed record PingMessage(string Payload) : IMessage;

public sealed class PingHandler : IMessageHandler<PingMessage>
{
    private readonly ILogger<PingHandler> _logger;

    public PingHandler(ILogger<PingHandler> logger) => _logger = logger;

    public ValueTask<Result> HandleAsync(PingMessage message, MessageContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Received Ping message: {Payload} (MessageId: {MessageId})", message.Payload, context.Metadata.MessageId);
        return ValueTask.FromResult(Result.Success());
    }
}
```

---

## Recipe 2: Resiliency with Circuit Breaker and Handler Timeouts

### Problem
Protect downstream services and avoid thread pool exhaustion when external dependencies experience outages or latency spikes.

### Solution
Configure `AddCircuitBreaker` and `AddHandlerTimeout` via `MessagingOptionsBuilder`.

```csharp
builder.Services.AddMessaging(options =>
{
    // Fast-fails incoming messages after 5 consecutive failures, testing recovery after 30 seconds
    options.AddCircuitBreaker(cb =>
    {
        cb.FailureThreshold = 5;
        cb.BreakDuration = TimeSpan.FromSeconds(30);
        cb.SamplingDuration = TimeSpan.FromSeconds(60);
    });

    // Cancels execution if a message handler exceeds 10 seconds
    options.AddHandlerTimeout(TimeSpan.FromSeconds(10));
});
```

---

## Recipe 3: High-Throughput Batch Publishing

### Problem
Publishing hundreds of messages per second while minimizing network roundtrips and optimizing broker throughput.

### Solution
Use `IMessagePublisher.PublishBatchAsync<TMessage>` which utilizes `IBatchMessageTransport` on supported drivers.

```csharp
public sealed class OrderBatchService
{
    private readonly IMessagePublisher _publisher;

    public OrderBatchService(IMessagePublisher publisher)
    {
        _publisher = publisher;
    }

    public async Task<Result> PublishPendingOrdersAsync(
        IEnumerable<OrderCreatedMessage> orders,
        CancellationToken cancellationToken = default)
    {
        var options = new MessagePublishOptions
        {
            Destination = "orders.batch.topic"
        };
        return await _publisher.PublishBatchAsync(orders, options, cancellationToken);
    }
}
```

---

## Recipe 4: Schema Evolution and Message Upcasting

### Problem
A message contract has evolved from V1 to V2, but legacy V1 messages remain queued in the broker.

### Solution
Implement `IMessageUpcaster` and register it with `AddMessageUpcaster` and `options.AddUpcasting()`.

```csharp
// 1. Configure Program.cs
builder.Services.AddMessaging(options =>
{
    options.AddUpcasting();
});
builder.Services.AddMessageUpcaster<OrderPlacedV1, OrderPlacedV2, OrderPlacedV1ToV2Upcaster>();
builder.Services.AddMessageHandler<OrderPlacedV2, OrderPlacedV2Handler>();

// 2. Contracts
[MessageType("orders.placed.v1")]
public sealed record OrderPlacedV1(Guid OrderId, decimal Amount) : IMessage;

[MessageType("orders.placed.v2")]
public sealed record OrderPlacedV2(Guid OrderId, decimal Amount, string Currency) : IMessage;

// 3. Typed Upcaster: receives the old contract, returns the new contract
public sealed class OrderPlacedV1ToV2Upcaster : IMessageUpcaster<OrderPlacedV1, OrderPlacedV2>
{
    public OrderPlacedV2 Upcast(OrderPlacedV1 oldMessage, TransportMessageMetadata metadata)
        => new(oldMessage.OrderId, oldMessage.Amount, "USD");
}
```

---

## Recipe 5: Domain Events Bridge with `EricksonLopez.Messaging.Events`

### Problem
Propagate domain events generated in domain aggregates (`EricksonLopez.Events`) to external distributed message brokers.

### Solution
Use `EricksonLopez.Messaging.Events` and `services.AddMessagingEventPublisher()`.

```csharp
using EricksonLopez.Events.Contracts;
using EricksonLopez.Messaging.Events;

builder.Services.AddMessaging();
builder.Services.AddMessagingEventPublisher(options =>
{
    // ThrowOnFailure: true (default) rethrows publish errors as exceptions.
    // Set to false to suppress exceptions and handle Result.Failure instead.
    options.ThrowOnFailure = false;

    // DestinationResolver: optional Func<Type, string> for per-type destination routing.
    // When null, the [MessageType] attribute value is used as the destination.
    options.DestinationResolver = eventType => $"{eventType.Name.ToLowerInvariant()}.v1";
});
```

---

## Recipe 6: Azure Service Bus with Managed Identity (DefaultAzureCredential)

### Problem
Connect to Azure Service Bus without hardcoding static connection strings or secrets.

### Solution
Configure `AddAzureServiceBusMessaging` using `FullyQualifiedNamespace` and `Azure.Identity`.

```csharp
using Azure.Identity;
using EricksonLopez.Messaging.AzureServiceBus;

builder.Services.AddMessaging();
builder.Services.AddAzureServiceBusMessagingTransport(options =>
{
    // Option A: Connection string authentication
    // options.ConnectionString = "Endpoint=sb://...;SharedAccessKeyName=...;SharedAccessKey=...";

    // Option B: Managed Identity / DefaultAzureCredential (recommended for production)
    options.FullyQualifiedNamespace = "myservicebus.servicebus.windows.net";
    options.Credential = new DefaultAzureCredential();
});
```

> **Note**: `AzureServiceBusTransportOptions` properties are `ConnectionString`, `FullyQualifiedNamespace`, and `Credential` (TokenCredential).

---

## Recipe 7: RabbitMQ with QoS Prefetch and Publisher Confirms

### Problem
Configure RabbitMQ with AMQP 0-9-1, publisher acknowledgements, and dead lettering.

```csharp
using EricksonLopez.Messaging.RabbitMQ;

builder.Services.AddMessaging();
builder.Services.AddRabbitMqMessagingTransport(options =>
{
    options.HostName     = "rabbitmq.internal.local";
    options.Port         = 5672;
    options.VirtualHost  = "/production";
    options.UserName     = "messaging_user";
    options.Password     = "secure_password";
    options.ExchangeName = "app.direct";
});
```

> **Note**: `RabbitMqTransportOptions` properties are limited to `HostName`, `Port`, `VirtualHost`, `UserName`, `Password`, and `ExchangeName`.

---

## Recipe 8: Apache Kafka with Partition Key Routing

### Problem
Ensure strictly ordered partition processing by customer ID or device ID.

```csharp
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Kafka;

builder.Services.AddMessaging();
builder.Services.AddKafkaMessagingTransport(options =>
{
    options.BootstrapServers = "kafka-1:9092,kafka-2:9092";
    options.GroupId          = "orders-consumer-group";
    options.ClientId         = "orders-producer"; // optional, used for broker logging
    options.EnableAutoCommit = false;             // manual offset commit after handler Ack
});

[MessageType("telemetry.device-metric.v1")]
public sealed record DeviceMetricMessage(
    [property: PartitionKey] string DeviceId,
    double Temperature,
    DateTimeOffset Timestamp) : IMessage;
```

> **Note**: `KafkaTransportOptions` properties are `BootstrapServers`, `GroupId`, `ClientId`, and `EnableAutoCommit`.

---

## Recipe 9: AWS SQS with Long Polling and Delayed Deferral

### Problem
Consume AWS SQS queues efficiently using long polling to reduce API costs.

```csharp
using EricksonLopez.Messaging.AwsSqs;

builder.Services.AddMessaging();
builder.Services.AddAwsSqsMessagingTransport(options =>
{
    options.Region             = "us-east-1";
    options.ServiceUrl         = null; // set to LocalStack endpoint for local testing
    options.WaitTimeSeconds    = 20;  // 20s long polling
    options.MaxNumberOfMessages = 10;
});
```

> **Note**: `AwsSqsTransportOptions` properties are `Region`, `ServiceUrl`, `WaitTimeSeconds`, and `MaxNumberOfMessages`.

---

## Recipe 10: Custom Context & Security Middleware

### Problem
Inspect headers or validate tenant isolation before invoking message handlers.

```csharp
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Middleware;
using EricksonLopez.Result;

public sealed class TenantValidationMiddleware : IMessageMiddleware
{
    public async ValueTask<Result> InvokeAsync(
        MessageContext context,
        MessageExecutionDelegate next,
        CancellationToken cancellationToken = default)
    {
        // Access transport headers from the message context
        if (context.Metadata.Headers is null ||
            !context.Metadata.Headers.TryGetValue("X-Tenant-Id", out var tenantId) ||
            string.IsNullOrWhiteSpace(tenantId))
        {
            return Result.Failure(Error.Validation("Security.MissingTenant", "Incoming message lacks X-Tenant-Id header."));
        }

        // Store resolved tenant in the ambient Items dictionary for downstream handlers
        context.Items["TenantId"] = tenantId;
        return await next(context, cancellationToken);
    }
}
```

---

## Recipe 11: Distributed Tracing with OpenTelemetry

### Problem
Export messaging spans and metrics to OpenTelemetry collectors (OTLP).

```csharp
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using EricksonLopez.Messaging.OpenTelemetry;

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddMessagingInstrumentation();
        tracing.AddOtlpExporter();
    });
```

---

## Recipe 12: In-Memory Integration Testing with `InMemoryTestHarness`

### Problem
Test message publishing and consumption without external Docker containers.

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Testing;
using EricksonLopez.Messaging.Transport;
using Xunit;

public sealed class OrderPublishingTests
{
    [Fact]
    public async Task PublishRawAsync_TracksMessageInTestHarness()
    {
        // InMemoryTestHarness implements IMessageTransport directly.
        // Use it as a transport substitute and verify messages at the raw transport level.
        var harness = new InMemoryTestHarness();

        var metadata = TransportMessageMetadata.Create(
            messageType: "diagnostics.ping.v1",
            correlationId: "test-correlation-id");

        var payload = System.Text.Encoding.UTF8.GetBytes("{\"Payload\":\"TEST-PAYLOAD\"}");
        var result = await harness.PublishRawAsync(
            destination: "diagnostics.ping.v1",
            payload: payload,
            metadata: metadata);

        Assert.True(result.IsSuccess);
        Assert.Single(harness.PublishedMessages);
        Assert.Equal("diagnostics.ping.v1", harness.PublishedMessages[0].Metadata.MessageType);
        Assert.Equal("diagnostics.ping.v1", harness.PublishedMessages[0].Destination);
    }
}
```
