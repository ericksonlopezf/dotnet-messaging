# ADR-018: Circuit Breaker Middleware for Consumer Pipeline Resilience

## Status
Accepted — August 2026

## Date
2026-09-04

## Context
When a downstream dependency (database, external HTTP service, third-party API) becomes degraded or unresponsive, message handlers begin failing. Without a circuit breaker, the consumer pipeline will:

1. Continue receiving and attempting messages from the broker.
2. Fail each one, triggering `NackRequeue` on every message.
3. Saturate the broker queue with re-enqueued messages that will continue failing.
4. Potentially cause thread pool and semaphore exhaustion due to continuous failure loops.

MassTransit addresses this with `UseCircuitBreaker()` on the consumer pipeline. NServiceBus and Rebus provide equivalent mechanisms. The absence of a circuit breaker in `EricksonLopez.Messaging` was identified as **GAP-01** in the functional parity audit (August 2026).

## Decision

Implement `CircuitBreakerMiddleware : IMessageMiddleware` in the core `EricksonLopez.Messaging` package with the following design:

### State machine
Three states with thread-safe transitions guarded by `lock`:

| State | Behavior |
|---|---|
| **Closed** (normal) | Messages processed normally. Failures increment `_consecutiveFailures`. |
| **Open** (tripped) | All messages immediately rejected with `Result.Failure`. No downstream invocation. |
| **HalfOpen** (recovering) | Single probe message allowed through. Success → Closed. Failure → Open again. |

### Transition rules
- **Closed → Open**: `_consecutiveFailures >= FailureThreshold`
- **Open → HalfOpen**: `elapsed >= BreakDuration` (measured via `TimeProvider`)
- **HalfOpen → Closed**: First successful handler result
- **HalfOpen → Open**: Any failure (including exceptions)

### Failure counting
Both `Result.IsFailure` outcomes AND uncaught exceptions (when cancellation is not requested) are counted as failures. This ensures that transport-level exceptions (connection refused, socket timeout) also trip the breaker.

### Sliding window (`SamplingDuration`)
A `_firstFailureTimestamp` field (of type `long`, storing `TimeProvider.GetTimestamp()`) is tracked alongside `_consecutiveFailures`. When a new failure is recorded, if `TimeProvider.GetElapsedTime(_firstFailureTimestamp) >= SamplingDuration`, the counter is reset before incrementing. This prevents stale failures accumulated during quiet periods from triggering the breaker. A successful execution always resets both `_consecutiveFailures` and `_firstFailureTimestamp` to zero.

### `CircuitBreakerOptions`
```csharp
public sealed class CircuitBreakerOptions
{
    public int FailureThreshold { get; set; } = 5;
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30); // sliding window
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeProvider? TimeProvider { get; set; }
    public Func<Error, bool>? FailureFilter { get; set; }
}
```

### DI integration
Registered via `MessagingOptionsBuilder.AddCircuitBreaker(Action<CircuitBreakerOptions>? configure = null)`. Not added to the default pipeline — opt-in only.

### Error code
When the circuit is Open, returns `Result.Failure(Error.Failure("Messaging.CircuitBreaker.Open", ...))`. This signal causes `MessageConsumer` to nack the message (requeue or dead-letter, depending on downstream configuration).

## Alternatives Considered

### Alternative A: Use Microsoft.Extensions.Resilience / Polly circuit breaker
**Rejected.** Polly's `ResiliencePipeline` adds a heavyweight dependency and is primarily designed for outbound HTTP calls. The `HttpClient`-oriented API does not fit cleanly into the `IMessageMiddleware` contract (which returns `Result`, not exceptions). More importantly, it would introduce a non-AOT-safe reflection dependency.

### Alternative B: Circuit breaker per message type
**Deferred.** The current implementation is a single shared circuit breaker per middleware instance. Per-message-type circuit breakers require a `ConcurrentDictionary<string, CircuitState>` keyed by `MessageType`, which is a valid future enhancement but adds significant complexity. The single-breaker model covers the most common scenario (one transport, one downstream dependency).

### Alternative C: Sliding window counter instead of consecutive failures
**Implemented.** `SamplingDuration` was originally deferred (v1.0) but was subsequently implemented by tracking `_firstFailureTimestamp`. The current implementation uses a consecutive-failure model with a temporal observation window: if a new failure arrives after `SamplingDuration` has elapsed since the first failure in the current sequence, the counter resets. This is simpler than a ring buffer while still preventing long-idle stale failures from contributing to the threshold.

## Consequences

- **Positive**: Prevents queue saturation during downstream outages without requiring any external dependency.
- **Positive**: `TimeProvider`-injectable design enables deterministic unit testing of all state transitions.
- **Positive**: Non-breaking additive change — zero impact on existing consumers not using `AddCircuitBreaker()`.
- **Positive**: No new NuGet dependencies. Pure implementation using BCL primitives.
- **Trade-off**: Single shared circuit breaker per instance means all message types share the same breaker state. Acceptable for v1.

## References
- MassTransit: `UseCircuitBreaker()` — https://masstransit.io/documentation/configuration/middleware/circuit-breaker
- Microsoft.Extensions.Resilience: `ResiliencePipelineBuilder.AddCircuitBreaker()`
- `CircuitBreakerMiddleware.cs` — `src/EricksonLopez.Messaging/Middleware/CircuitBreakerMiddleware.cs`
- `CircuitBreakerOptions.cs` — `src/EricksonLopez.Messaging/Middleware/CircuitBreakerOptions.cs`
- Functional parity audit GAP-01
