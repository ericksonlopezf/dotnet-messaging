# Best Practices Guide — EricksonLopez.Messaging

Recommended architectural patterns and engineering practices for building robust, low-allocation, distributed systems with `EricksonLopez.Messaging`.

---

## 1. Message Contract Design

### Strict Immutability
Distributed messages represent immutable facts (events) or explicit instructions (commands). They must be **immutable**:

- **Recommended**: Use `sealed record` with positional parameters or `init`-only properties.
- **Prohibited**: Classes with mutable `get; set;` properties or mutable collection instances.

```csharp
// CORRECT
[MessageType("orders.order-shipped.v1")]
public sealed record OrderShippedEvent(
    Guid OrderId,
    string TrackingNumber,
    DateTimeOffset ShippedAtUtc) : IMessage;

// INCORRECT
public class OrderShippedEvent : IMessage
{
    public Guid OrderId { get; set; } // Mutable: prohibited
}
```

### Naming Conventions for `[MessageType]`
Use structured, explicitly versioned message type strings:
`<bounded-context>.<entity>-<event|command>.<version>`

- Example: `orders.order-created.v1`
- Example: `billing.process-invoice.v2`

---

## 2. Dependency Injection Lifecycle

### Enforce Scoped Handler Lifetimes (`ELMSG004`)
Every received message executes inside an isolated `IServiceScope`:

- **Rule**: Handlers must always be registered with `Scoped` lifetime (automatically handled by `AddMessageHandler<T, H>()` and `AddGeneratedMessagingHandlers()`).
- **Hazard**: Never register a message handler as `Singleton` or `Transient`, as doing so leads to concurrency races and leaks scoped dependencies such as `DbContext` or tenant context.

---

## 3. Functional Error Handling vs Exceptions

### Use `EricksonLopez.Result`
- Return `Result.Success()` when processing succeeds.
- Return `Result.Failure(Error.Validation(...))` for logical or validation failures. Non-retryable business validation failures are acknowledged by the consumer to prevent poison message retry storms.
- Return `Result.Failure(Error.Failure(...))` for transient infrastructure errors that should trigger the `CircuitBreakerMiddleware` or `RetryMiddleware`.

```csharp
public async ValueTask<Result> HandleAsync(
    ProcessOrderCommand message,
    MessageContext context,
    CancellationToken cancellationToken = default)
{
    if (message.Amount <= 0)
    {
        // Validation error: rejected functionally without throwing exceptions
        return Result.Failure(Error.Validation("Order.InvalidAmount", "Order amount must be greater than zero."));
    }

    return Result.Success();
}
```

---

## 4. Middleware Ordering & Resiliency

Configure middlewares in the optimal defensive order:

> **Note**: `AddMessaging()` automatically registers `TracingMiddleware`, `LoggingMiddleware`, and `ExceptionHandlingMiddleware` as defaults.
> The snippet below shows how to explicitly configure the full middleware ordering using the `MessagingOptionsBuilder` fluent API.
> Use this pattern when you need precise control over position, want to add `RetryMiddleware`, or are using a custom pipeline.

```csharp
builder.Services.AddMessaging(options =>
{
    // 1. Global exception capture (outermost)
    options.AddExceptionHandling();

    // 2. Structured logging & distributed tracing
    options.AddLogging();
    options.AddTracing();

    // 3. Circuit breaker (halts downstream traffic during outages)
    options.AddCircuitBreaker(cb =>
    {
        cb.FailureThreshold = 5;
        cb.BreakDuration = TimeSpan.FromSeconds(30);
    });

    // 4. Retry policy with exponential backoff and full-jitter randomization
    options.AddRetry(retry =>
    {
        retry.MaxRetries = 3;
        retry.InitialDelay = TimeSpan.FromMilliseconds(200);
    });

    // 5. Handler execution timeout (innermost execution boundary)
    options.AddHandlerTimeout(TimeSpan.FromSeconds(15));

    // 6. Upcasting (transform legacy schemas before handler invocation)
    options.AddUpcasting();
});
```

---

## 5. End-to-End Observability

- OpenTelemetry automatically injects and extracts the W3C `traceparent` header across all supported brokers.
- Configure tracing exporters in your application host to export messaging traces and metrics to Jaeger, Grafana Tempo, or Azure Application Insights.
