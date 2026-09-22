# Level 06: Error Handling & Resilience Architecture

## Overview
Level 06 demonstrates how `EricksonLopez.Messaging` guarantees bulletproof resilience without uncaught exceptions or poison message retry loops.

---

## 1. Functional Error Propagation (`Result.Failure`)

Handlers return `ValueTask<Result>` instead of throwing exceptions for domain or validation errors:

```csharp
public sealed class ValidateCustomerHandler : IMessageHandler<ValidateCustomerMessage>
{
    public ValueTask<Result> HandleAsync(
        ValidateCustomerMessage message,
        MessageContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.CustomerCode))
        {
            return ValueTask.FromResult(Result.Failure(
                Error.Validation("customer.invalid", "CustomerCode is required")));
        }

        return ValueTask.FromResult(Result.Success());
    }
}
```

---

## 2. Exponential Backoff with Jitter (`RetryMiddleware`)

Configurable via `RetryOptions`, `RetryMiddleware` intercepts transient failures and retries execution with exponential delays and full randomized jitter:

```csharp
builder.Services.AddMessaging(options =>
{
    options.AddRetry(retry =>
    {
        retry.MaxRetries = 3;
        retry.InitialDelay = TimeSpan.FromMilliseconds(100);
        retry.TimeProvider = TimeProvider.System;
    });
});
```

---

## 3. Fast-Failing Outage Protection (`CircuitBreakerMiddleware`)

When a downstream dependency suffers a persistent outage, `CircuitBreakerMiddleware` prevents thread pool saturation by short-circuiting handler calls:

```csharp
builder.Services.AddMessaging(options =>
{
    options.AddCircuitBreaker(cb =>
    {
        cb.FailureThreshold = 5;
        // SamplingDuration: failure observation window. The counter resets if failures are older than this.
        cb.SamplingDuration = TimeSpan.FromSeconds(30);
        cb.BreakDuration = TimeSpan.FromSeconds(60);
    });
});
```

- **Closed**: Normal execution; counts failures within the `SamplingDuration` sliding observation window.
- **Open**: Immediate fail-fast returning `messaging.circuit_breaker.open` error.
- **HalfOpen**: Probes downstream service with canary requests.

---

## 4. Dead Letter Queue Integration (`IDeadLetterQueue`)

Unrecoverable poison messages are routed to Dead Letter Queues with complete diagnostics captured via `DeadLetterReason`:

```csharp
var reason = DeadLetterReason.FromException(
    reasonCode: "FATAL_PROCESSING_ERROR",
    description: "Message handler exceeded max retry attempts",
    exception: ex);

await deadLetterQueue.ForwardToDeadLetterAsync(message, reason, context, cancellationToken);
```

Acknowledgement behavior is defined via `TransportAckResult`:
- `Ack`: Successfully processed; remove from queue.
- `NackRequeue`: Transient failure; requeue for redelivery.
- `DeadLetter`: Fatal poison message; route to dead letter storage.
