# Technical Cookbook & Integration Recipes — EricksonLopez.Messaging

Official collection of integration recipes and design patterns for real-world scenarios using `EricksonLopez.Messaging`. Each recipe addresses a specific requirement derived from the public API surface and follows the mandatory 6-part structure: **Problem**, **Solution**, **Complete Code**, **Explanation**, **Best Practices**, and **Common Pitfalls**.

---

## Recipe Index

1. [Recipe 1: Minimal Setup and Consumption with Generic Host](#recipe-1-minimal-setup-and-consumption-with-generic-host)
2. [Recipe 2: Resilience with Exponential Backoff and Full Jitter](#recipe-2-resilience-with-exponential-backoff-and-full-jitter)
3. [Recipe 3: Circuit Breaker Protection and Handler Timeouts](#recipe-3-circuit-breaker-protection-and-handler-timeouts)
4. [Recipe 4: High-Throughput Batch Publishing](#recipe-4-high-throughput-batch-publishing)
5. [Recipe 5: Message Schema Evolution via Upcasting](#recipe-5-message-schema-evolution-via-upcasting)
6. [Recipe 6: Idempotent Message Deduplication in At-Least-Once Delivery](#recipe-6-idempotent-message-deduplication-in-at-least-once-delivery)
7. [Recipe 7: Partition Key Routing with `[PartitionKey]` and `IPartitionKeyResolver`](#recipe-7-partition-key-routing-with-partitionkey-and-ipartitionkeyresolver)
8. [Recipe 8: Domain Event Bridging with `EricksonLopez.Messaging.Events`](#recipe-8-domain-event-bridging-with-ericksonlopezmessagingevents)
9. [Recipe 9: Dead-Letter Queue Routing and Forensic Diagnostics](#recipe-9-dead-letter-queue-routing-and-forensic-diagnostics)
10. [Recipe 10: Custom Context and Audit Interceptors via Middleware](#recipe-10-custom-context-and-audit-interceptors-via-middleware)
11. [Recipe 11: End-to-End Observability with OpenTelemetry (Traces and Metrics)](#recipe-11-end-to-end-observability-with-opentelemetry-traces-and-metrics)
12. [Recipe 12: In-Memory Integration Testing with `InMemoryTestHarness`](#recipe-12-in-memory-integration-testing-with-inmemorytestharness)

---

## Recipe 1: Minimal Setup and Consumption with Generic Host

### Problem
A .NET worker service or microservice needs to consume and publish strongly-typed messages with minimal memory allocation, Native AOT compatibility, and graceful shutdown without requiring external infrastructure for initial setup.

### Solution
Use `builder.Services.AddMessaging()` to register core in-memory messaging components and `builder.Services.AddMessageHandler<TMessage, THandler>()` to bind consumer handlers.

### Complete Code
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// 1. Register core messaging infrastructure and handlers
builder.Services.AddMessaging();
builder.Services.AddMessageHandler<OrderNotification, OrderNotificationHandler>();

var app = builder.Build();

// 2. Start host background consumers
await app.StartAsync();

// 3. Publish a strongly-typed message
var publisher = app.Services.GetRequiredService<IMessagePublisher>();
var result = await publisher.PublishAsync(new OrderNotification(Guid.NewGuid(), "ORD-1001", "Processed"));

if (result.IsSuccess)
{
    Console.WriteLine("Message published successfully.");
}

await Task.Delay(100);
await app.StopAsync();

[MessageType("orders.notification.v1")]
public sealed record OrderNotification(Guid OrderId, string OrderNumber, string Status) : IMessage;

public sealed class OrderNotificationHandler : IMessageHandler<OrderNotification>
{
    private readonly ILogger<OrderNotificationHandler> _logger;

    public OrderNotificationHandler(ILogger<OrderNotificationHandler> logger) => _logger = logger;

    public ValueTask<Result> HandleAsync(OrderNotification message, MessageContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Notification received: {OrderNumber} status={Status} (MsgId: {Id})",
            message.OrderNumber, message.Status, context.Metadata.MessageId);
        return ValueTask.FromResult(Result.Success());
    }
}
```

### Explanation
`AddMessaging()` registers the Native AOT serializer (`NativeAotJsonSerializer`), default in-memory transport (`InMemoryMessageTransport`), dispatcher (`DefaultMessageDispatcher`), and background consumer hosted service (`MessagingConsumerHostedService`). When calling `AddMessageHandler<T, H>`, the handler is registered in DI and added to the dispatcher's internal type-routing table.

### Best Practices
- Always define messages as immutable `sealed record` types.
- Decorate every message contract with explicit `[MessageType("domain.event.v1")]` attributes.
- Return `ValueTask<Result>` from handlers to eliminate heap allocation on synchronous completions.

### Common Pitfalls
- Registering `AddMessageHandler` after building the host container (`builder.Build()`). All handlers must be registered prior to container build.
- Forgetting that the default in-memory transport does not persist messages across process restarts.

---

## Recipe 2: Resilience with Exponential Backoff and Full Jitter

### Problem
Message processing operations occasionally fail due to transient errors (temporary database locks, network timeouts). If all consumers retry simultaneously on fixed intervals, a thundering herd problem overloads downstream services.

### Solution
Configure `options.AddRetry()` with exponential backoff (`InitialDelay`), maximum delay ceiling (`MaxDelay`), and full randomized jitter based on `TimeProvider`.

### Complete Code
```csharp
using System;
using EricksonLopez.Messaging;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMessaging(options =>
{
    options.AddRetry(retry =>
    {
        retry.MaxRetries = 4;
        retry.InitialDelay = TimeSpan.FromMilliseconds(150);
        retry.MaxDelay = TimeSpan.FromSeconds(2);
        retry.TimeProvider = TimeProvider.System;
        retry.ShouldRetry = error => error.Code.StartsWith("Transient.");
    });
});
```

### Explanation
`RetryMiddleware` evaluates the `Result` returned by downstream pipeline handlers. If `Result.IsFailure` and the `ShouldRetry` predicate returns `true`, it computes delay as `Random.Shared.NextDouble() * (initialDelay * 2^attempt)` capped by `MaxDelay` and suspends execution using `TimeProvider.Delay`. If retries are exhausted, the final error is returned to the consumer for ack/nack decision.

### Best Practices
- Always supply a `TimeProvider` (e.g., `TimeProvider.System` or `FakeTimeProvider` in tests) to enable deterministic unit testing without artificial delays.
- Limit `MaxRetries` to a conservative number (3 to 5) to avoid holding consumer threads and broker lock leases.

### Common Pitfalls
- Retrying permanent validation or domain errors (`Validation.InvalidData`). Always configure `ShouldRetry` to filter exclusively on transient errors.

---

## Recipe 3: Circuit Breaker Protection and Handler Timeouts

### Problem
A critical downstream dependency (payment gateway, third-party API) experiences an outage. Continuing to attempt message processing exhausts thread pool resources and overburdens the failing service. Furthermore, hanging handlers block consumer threads indefinitely.

### Solution
Combine `AddCircuitBreaker` for fast-fail protection and `AddHandlerTimeout` to enforce strict message processing deadlines.

### Complete Code
```csharp
using System;
using EricksonLopez.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMessaging(options =>
{
    // Fast-fail: opens after 3 consecutive failures within 30s; tests recovery after 15s
    options.AddCircuitBreaker(cb =>
    {
        cb.FailureThreshold = 3;
        // SamplingDuration: observation window — failures older than this are not counted toward FailureThreshold.
        cb.SamplingDuration = TimeSpan.FromSeconds(30);
        cb.BreakDuration = TimeSpan.FromSeconds(15);
        cb.TimeProvider = TimeProvider.System;
        // FailureFilter — only count specific error codes toward the threshold.
        // When null, all Result failures are counted (default behaviour).
        cb.FailureFilter = err => err.Code != "Customer.InvalidData"; // ignore validation errors
    });

    // Deadline: cancels processing if handler execution exceeds 5 seconds
    options.AddHandlerTimeout(TimeSpan.FromSeconds(5));
});
```

### Explanation
`CircuitBreakerMiddleware` implements a `Closed` → `Open` → `HalfOpen` state machine. In the `Open` state, any incoming message fails fast immediately with `Result.Failure(Error.Failure("CircuitBreaker.Open"))` without calling the handler. `HandlerTimeoutMiddleware` creates a linked `CancellationTokenSource` that triggers cancellation if handler execution exceeds the configured duration.

`FailureFilter` is an optional predicate that controls whether a specific `Error` counts toward the failure threshold. When `null`, all `Result` failures are counted. Setting it allows you to exclude expected domain errors (e.g., validation failures) from tripping the circuit breaker.

### Best Practices
- Position `CircuitBreakerMiddleware` outside `RetryMiddleware` in the pipeline so individual retries are not counted as separate circuit breaker failures.
- Always supply a `FailureFilter` to avoid counting business validation errors (which are expected) as infrastructure failures.
- Ensure handlers actively monitor `cancellationToken.ThrowIfCancellationRequested()` during I/O operations.

### Common Pitfalls
- Setting a timeout shorter than nominal P99 latency during traffic spikes, triggering false-positive cancellations.
- Forgetting to set `FailureFilter`, which causes validation errors to incorrectly trip the circuit breaker.

---

## Recipe 4: High-Throughput Batch Publishing

### Problem
A batch data synchronization service needs to emit thousands of events per second without saturating network connections with thousands of individual TCP roundtrips.

### Solution
Use `IMessagePublisher.PublishBatchAsync<TMessage>` or `SendBatchAsync<TMessage>`, which leverages `IBatchMessageTransport` on transports supporting native batching.

### Complete Code
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;

public sealed class CatalogSyncService
{
    private readonly IMessagePublisher _publisher;

    public CatalogSyncService(IMessagePublisher publisher) => _publisher = publisher;

    public async ValueTask<Result> PublishInventoryUpdatesAsync(
        IEnumerable<InventoryUpdatedEvent> updates,
        CancellationToken cancellationToken = default)
    {
        var options = new MessagePublishOptions
        {
            TenantId = "tenant-global",
            Headers = new Dictionary<string, string> { ["X-Origin"] = "ERP-Sync" }
        };

        return await _publisher.PublishBatchAsync(updates, options, cancellationToken);
    }
}

[MessageType("inventory.updated.v1")]
public sealed record InventoryUpdatedEvent(string Sku, int Stock, decimal Price) : IMessage;
```

### Explanation
When the underlying transport implements `IBatchMessageTransport` (such as `AzureServiceBusMessageTransport`), `MessagePublisher` groups binary payloads into a single wire request (`PublishBatchRawAsync`), significantly reducing network overhead.

### Best Practices
- Partition large datasets into manageable batches (e.g., 100 to 500 items) to remain safely within broker message frame limits (e.g., 1 MB on Azure Service Bus).

### Common Pitfalls
- Assuming batch delivery is atomic across all brokers. In distributed systems, partial batch ingestion may occur if individual messages fail validation.

---

## Recipe 5: Message Schema Evolution via Upcasting

### Problem
An event contract evolves from version 1 (`OrderPlacedV1`) to version 2 (`OrderPlacedV2`) by introducing mandatory fields (`Currency`, `Channel`). Unprocessed legacy messages remain in broker queues. Modifying broker state or cluttering consumer handlers with multi-version branching violates Single Responsibility.

### Solution
Implement `IMessageUpcaster<OrderPlacedV1, OrderPlacedV2>`, register it with `AddMessageUpcaster`, and activate `options.AddUpcasting()`.

### Complete Code
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMessaging(options =>
{
    options.AddUpcasting(); // Activates MessageUpcastingMiddleware
});

// Register upcaster and handler for the current version (V2)
builder.Services.AddMessageUpcaster<OrderPlacedV1, OrderPlacedV2, OrderPlacedUpcaster>();
builder.Services.AddMessageHandler<OrderPlacedV2, OrderPlacedV2Handler>();

var app = builder.Build();

[MessageType("orders.placed.v1")]
public sealed record OrderPlacedV1(Guid OrderId, string OrderNumber, decimal Total) : IMessage;

[MessageType("orders.placed.v2")]
public sealed record OrderPlacedV2(Guid OrderId, string OrderNumber, decimal Total, string Currency, string Channel) : IMessage;

public sealed class OrderPlacedUpcaster : IMessageUpcaster<OrderPlacedV1, OrderPlacedV2>
{
    public OrderPlacedV2 Upcast(OrderPlacedV1 oldMessage, TransportMessageMetadata metadata)
    {
        return new OrderPlacedV2(
            OrderId: oldMessage.OrderId,
            OrderNumber: oldMessage.OrderNumber,
            Total: oldMessage.Total,
            Currency: "USD",       // Safe default for legacy data
            Channel: "Legacy-Web"  // Safe default
        );
    }
}

public sealed class OrderPlacedV2Handler : IMessageHandler<OrderPlacedV2>
{
    public ValueTask<Result> HandleAsync(OrderPlacedV2 message, MessageContext context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Processing V2: {message.OrderNumber} in {message.Currency} via {message.Channel}");
        return ValueTask.FromResult(Result.Success());
    }
}
```

### Explanation
When an incoming message with `MessageType == "orders.placed.v1"` arrives, the deserializer loads it as `OrderPlacedV1`. `MessageUpcastingMiddleware` detects the registered `IMessageUpcaster` for `OrderPlacedV1 → OrderPlacedV2`, executes the deterministic transformation, and replaces `context.Message`. The dispatcher invokes only `OrderPlacedV2Handler`.

### Best Practices
- Keep upcasters deterministic and side-effect-free (pure functions).
- Do not make external database or network calls inside an upcaster; use safe defaults or metadata headers.

### Common Pitfalls
- Forgetting to call `options.AddUpcasting()`. Without it, the middleware is not included in the pipeline and V1 messages fail if no V1 handler is registered.

---

## Recipe 6: Idempotent Message Deduplication in At-Least-Once Delivery

### Problem
In distributed messaging with at-least-once delivery, acknowledgment network failures or broker retries can redeliver identical messages. Duplicate processing of payments or orders causes severe data corruption.

### Solution
Activate `options.AddDeduplication()` and configure `IMessageDeduplicationStore` (or the built-in `InMemoryMessageDeduplicationStore`).

### Complete Code
```csharp
using System;
using EricksonLopez.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMessaging(options =>
{
    options.AddDeduplication(dedup =>
    {
        dedup.Enabled = true;
        dedup.Expiration = TimeSpan.FromMinutes(10); // Deduplication retention window
    });
});
```

### Explanation
`MessageDeduplicationMiddleware` calls `IMessageDeduplicationStore.TryAcquireAsync(metadata.MessageId, expiration)` before executing the pipeline. If the identifier was already acquired within the retention window, processing is skipped and `Result.Success()` is returned immediately, triggering a `TransportAckResult.Ack` back to the broker without re-executing handler logic.

### Best Practices
- In multi-instance deployments, replace `InMemoryMessageDeduplicationStore` with a distributed provider (Redis, PostgreSQL) implementing `IMessageDeduplicationStore`.
- Ensure publishers assign deterministic `MessageId` values when resending application-level retry messages.

### Common Pitfalls
- Configuring an expiration TTL shorter than the broker's maximum redelivery window.

---

## Recipe 7: Partition Key Routing with `[PartitionKey]` and `IPartitionKeyResolver`

### Problem
In partitioned messaging systems (Apache Kafka, Azure Service Bus Topics), messages belonging to the same entity or partition key (e.g., customer, region) must be processed strictly sequentially on the same partition.

### Solution
Annotate message contract properties with `[PartitionKey]` or implement a custom `IPartitionKeyResolver` for dynamic resolution.

### Complete Code
```csharp
using System;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;

// Option A: Declarative via [PartitionKey]
[MessageType("orders.partitioned.v1")]
public sealed record PartitionedOrderMessage(
    Guid OrderId,
    [property: PartitionKey] string Region
) : IMessage;

// Option B: Imperative via IPartitionKeyResolver
public sealed class CustomOrderPartitionKeyResolver : IPartitionKeyResolver
{
    public string? Resolve<TMessage>(TMessage message) where TMessage : notnull
    {
        if (message is PartitionedOrderMessage order)
        {
            return $"partition-{order.Region.ToLowerInvariant()}";
        }
        return null;
    }
}
```

### Explanation
When `PublishAsync` or `SendAsync` is called, `MessagePublisher` checks for a registered `IPartitionKeyResolver` that returns a non-null key. If none is found, it inspects properties decorated with `[PartitionKey]`. The resolved key is assigned to `TransportMessageMetadata.PartitionKey` and used by transport drivers as the hashing partition key.

### Best Practices
- Select partition keys with high cardinality (e.g., `CustomerId` or `TenantId + Region`) to ensure uniform load distribution across broker partitions.

### Common Pitfalls
- Using low-cardinality keys (e.g., a binary flag), creating a "hot partition" bottleneck while other partitions remain idle.

---

## Recipe 8: Domain Event Bridging with `EricksonLopez.Messaging.Events`

### Problem
The application domain layer raises events using the `IEventPublisher` abstraction from `EricksonLopez.Events.Contracts`. These events must automatically publish to the distributed messaging broker without coupling domain models to messaging transport packages.

### Solution
Use `builder.Services.AddMessagingEventPublisher()` from `EricksonLopez.Messaging.Events`.

### Complete Code
```csharp
using System;
using EricksonLopez.Messaging;
using EricksonLopez.Messaging.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMessaging();

// Activate event-to-messaging bridge
builder.Services.AddMessagingEventPublisher(options =>
{
    options.ThrowOnFailure = true;
    options.DestinationResolver = eventType => $"events.{eventType.Name.ToLowerInvariant()}.v1";
});
```

### Explanation
`MessagingEventPublisher` implements `IEventPublisher`. When the domain raises an event, the publisher evaluates `DestinationResolver` to derive destination topic/queue names and delegates to `IMessagePublisher.PublishAsync`. When `ThrowOnFailure` is enabled, transport publishing failures throw an exception to trigger transactional unit-of-work rollback.

### Best Practices
- Configure `ThrowOnFailure = true` in transactional operations to prevent committing database changes if domain events cannot be dispatched.

### Common Pitfalls
- Modifying domain event class names without accounting for schema naming rules configured in `DestinationResolver`.

---

## Recipe 9: Dead-Letter Queue Routing and Forensic Diagnostics

### Problem
A message contains corrupted or unrecoverable data causing continuous handler failure (poison message). It must be routed to a dead-letter queue (DLQ) capturing exception details, root cause, and UTC timestamp for forensic diagnostics.

### Solution
Implement `IDeadLetterQueue` and use `DeadLetterReason` to encapsulate error details.

### Complete Code
```csharp
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;

public sealed class DatabaseDeadLetterQueue : IDeadLetterQueue
{
    public ValueTask<Result> ForwardToDeadLetterAsync<TMessage>(
        TMessage message,
        DeadLetterReason reason,
        MessageContext? context = null,
        CancellationToken cancellationToken = default) where TMessage : notnull
    {
        Console.WriteLine($"[Typed DLQ] Message {typeof(TMessage).Name} dead-lettered. Reason: {reason.ReasonCode} - {reason.Description}");
        return ValueTask.FromResult(Result.Success());
    }

    public ValueTask<Result> ForwardRawToDeadLetterAsync(
        ReadOnlyMemory<byte> rawPayload,
        DeadLetterReason reason,
        TransportMessageMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[Raw DLQ] Payload ({rawPayload.Length} bytes) dead-lettered. Reason: {reason.ReasonCode}");
        return ValueTask.FromResult(Result.Success());
    }
}
```

### Explanation
When a handler returns an unrecoverable failure or the broker triggers dead-lettering, `IDeadLetterQueue` is invoked. `DeadLetterReason.FromException(code, desc, ex)` captures a complete diagnostic snapshot including stack trace and UTC timestamp.

### Best Practices
- Store the raw binary payload (`rawPayload`) alongside headers to enable message replay once application fixes are deployed.

### Common Pitfalls
- Silently discarding poison messages instead of forwarding them to dead-letter storage with forensic metadata.

---

## Recipe 10: Custom Context and Audit Interceptors via Middleware

### Problem
Every incoming message must be audited for execution time, message identifier, and tenant context uniformly across all consumer handlers.

### Solution
Implement `IMessageMiddleware` and register it via `options.AddMiddleware<TMiddleware>()`.

### Complete Code
```csharp
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Messaging;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Middleware;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;

public sealed class AuditMiddleware : IMessageMiddleware
{
    private readonly ILogger<AuditMiddleware> _logger;

    public AuditMiddleware(ILogger<AuditMiddleware> logger) => _logger = logger;

    public async ValueTask<Result> InvokeAsync(
        MessageContext context,
        MessageExecutionDelegate next,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[Audit] Starting message {MessageId} type={Type} tenant={Tenant}",
            context.Metadata.MessageId, context.Metadata.MessageType, context.Metadata.TenantId);

        var result = await next(context, cancellationToken);
        sw.Stop();

        if (result.IsSuccess)
        {
            _logger.LogInformation("[Audit] Message {MessageId} processed successfully in {ElapsedMs}ms",
                context.Metadata.MessageId, sw.ElapsedMilliseconds);
        }
        else
        {
            _logger.LogWarning("[Audit] Message {MessageId} failed in {ElapsedMs}ms with code {ErrorCode}",
                context.Metadata.MessageId, sw.ElapsedMilliseconds, result.Error.Code);
        }

        return result;
    }
}
```

### Explanation
Middleware follows the Russian Doll pipeline pattern. Calling `await next(context, cancellationToken)` delegates to the next interceptor or final handler. Middleware can inspect or enrich `context.Items` before or after execution.

### Best Practices
- Never throw exceptions from middleware; catch errors and return structured `Result.Failure`.
- Resolve scoped dependencies through `context.ServiceProvider`.

### Common Pitfalls
- Forgetting to call `await next(...)`, which short-circuits the pipeline and stops message processing.

---

## Recipe 11: End-to-End Observability with OpenTelemetry (Traces and Metrics)

### Problem
Production operations require real-time visibility into message bus performance: tracking published, consumed, and failed message rates, handler latency distributions, and distributed tracing via W3C Trace Context.

### Solution
Add package `EricksonLopez.Messaging.OpenTelemetry` and invoke `AddMessagingInstrumentation()` inside both `WithTracing()` and `WithMetrics()` builders.

### Complete Code
```csharp
using EricksonLopez.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMessaging();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddMessagingInstrumentation(); // Instruments ActivitySource
        tracing.AddConsoleExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMessagingInstrumentation(); // Instruments Meter
        metrics.AddConsoleExporter();
    });
```

### Explanation
`MessagingDiagnostics` defines the `ActivitySourceName` (`"EricksonLopez.Messaging"`) and `MeterName` (`"EricksonLopez.Messaging"`). Standard counters (`messages.published`, `messages.received`, `messages.failed`) and histograms (`processing.duration`) emit without reflection overhead.

### Best Practices
- Export telemetry to an OpenTelemetry Collector via OTLP in production environments.

### Common Pitfalls
- Enabling tracing without registering metrics (`WithMetrics`), losing visibility into aggregate throughput and queue depth indicators.

---

## Recipe 12: In-Memory Integration Testing with `InMemoryTestHarness`

### Problem
Publisher and consumer workflows must be verified in CI unit and integration test suites with zero external dependencies, complete determinism, and instant execution without Docker or cloud message brokers.

### Solution
Instantiate `InMemoryTestHarness` from `EricksonLopez.Messaging.Testing` and utilize async wait helpers `WaitUntilPublishedAsync` and `WaitUntilConsumedAsync`.

### Complete Code
```csharp
using System;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Testing;
using EricksonLopez.Messaging.Transport;

public static class TestHarnessSample
{
    public static async Task RunTestAsync()
    {
        await using var harness = new InMemoryTestHarness();

        var metadata = TransportMessageMetadata.Create("orders.placed.v1");
        var payload = new byte[] { 1, 2, 3 };

        // 1. Publish raw message payload
        await harness.PublishRawAsync("orders.placed.v1", payload, metadata);

        // 2. Assert publication with asynchronous wait helper
        var published = await harness.WaitUntilPublishedAsync("orders.placed.v1", TimeSpan.FromMilliseconds(200));
        Console.WriteLine($"Published successfully: {published}");

        // 3. Subscribe consumer delegate
        await harness.SubscribeAsync(
            "orders.placed.v1",
            (bytes, meta, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            new TransportSubscriptionOptions());

        // 4. Assert consumption with asynchronous wait helper
        var consumed = await harness.WaitUntilConsumedAsync("orders.placed.v1", TimeSpan.FromMilliseconds(200));
        Console.WriteLine($"Consumed successfully: {consumed}");
        Console.WriteLine($"Any succeeded: {harness.ConsumedMessages.AnySucceeded("orders.placed.v1")}");
    }
}
```

### Explanation
`InMemoryTestHarness` implements `IMessageTransport` and `IAsyncDisposable`. It records all published events in `PublishedMessages` and consumed messages in `ConsumedMessages`. The `WaitUntilPublishedAsync` and `WaitUntilConsumedAsync` helpers use internal `TaskCompletionSource` notifications to avoid arbitrary `Task.Delay` pauses in tests.

### Best Practices
- Dispose the harness using `await using` to cleanly release in-memory channels.
- Use `OfType(messageType)` and `AnySucceeded` extension methods for clean and expressive assertions in xUnit or NUnit.

### Common Pitfalls
- Using fixed delays (`Thread.Sleep` or `Task.Delay`) in tests, leading to flaky test execution under CI runner load. Always use the harness's asynchronous completion signals.
