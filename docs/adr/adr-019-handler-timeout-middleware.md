# ADR-019: Handler Timeout Middleware via Linked CancellationToken

## Status
Accepted — August 2026

## Context
Message handler executions are inherently unbounded by default. A handler that hangs — due to a deadlock, a blocking external call that never returns, or an unresponsive downstream — will hold a `SemaphoreSlim` slot in `MessageConsumer` indefinitely. This leads to:

1. **Consumer starvation**: The semaphore prevents new messages from being processed as long as a slot is occupied by a hung handler.
2. **Invisible degradation**: The handler does not throw, so no retry or DLQ signal is produced. The message appears "in-flight" forever.
3. **Memory pressure**: The `IServiceScope` created per message stays alive for the duration.

The absence of a handler timeout was identified as **GAP-09** in the functional parity audit (August 2026).

MassTransit, Wolverine, and NServiceBus all provide mechanisms to enforce handler execution limits.

## Decision

Implement `HandlerTimeoutMiddleware : IMessageMiddleware` in the core `EricksonLopez.Messaging` package.

### Mechanism: Linked CancellationToken
The middleware creates a `CancellationTokenSource` with the configured timeout, then links it to the incoming `CancellationToken` (which signals graceful shutdown). The combined token is passed to the next middleware in the pipeline:

```csharp
using var timeoutCts = new CancellationTokenSource(timeout, _timeProvider);
using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
return await next(context, linkedCts.Token);
```

This approach:
- **Preserves graceful shutdown**: If the application shuts down, the host cancellation token fires and the linked token is cancelled — regardless of the timeout.
- **Detects timeout vs. host cancellation**: By checking `timeoutCts.IsCancellationRequested` in the `OperationCanceledException` catch clause, the middleware distinguishes between a timeout and a legitimate shutdown signal. Only timeouts produce a `Result.Failure`; shutdown cancellations propagate normally.

### `HandlerTimeoutOptions`
```csharp
public sealed class HandlerTimeoutOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeProvider? TimeProvider { get; set; }
}
```

Setting `Timeout` to `TimeSpan.Zero` or `Timeout.InfiniteTimeSpan` disables the middleware (pass-through).

### Error code
On timeout: `Result.Failure(Error.Failure("Messaging.Handler.Timeout", ...))`. This allows the pipeline to signal a controlled failure that triggers retry or DLQ logic downstream.

### DI integration
Registered via two overloads on `MessagingOptionsBuilder`:
- `AddHandlerTimeout(TimeSpan timeout)` — direct timeout value
- `AddHandlerTimeout(Action<HandlerTimeoutOptions>? configure)` — full options delegate

Not added to the default pipeline — opt-in only.

## Alternatives Considered

### Alternative A: `Task.WhenAny` with `Task.Delay`
**Rejected.** `Task.WhenAny(next(...), Task.Delay(timeout))` does not actually cancel the handler task. The handler continues executing in the background even after the timeout fires, consuming resources. Linked `CancellationToken` is the correct cooperative cancellation pattern.

### Alternative B: `CancellationTokenSource.CreateLinkedTokenSource` with Polly `TimeoutPolicy`
**Rejected.** Polly's timeout policy is well-established but adds a non-trivial dependency and is exception-based (`TimeoutRejectedException`). The `IMessageMiddleware` contract is Result-based. Wrapping `TimeoutRejectedException` into `Result.Failure` would be possible but introduces unnecessary complexity.

### Alternative C: Apply globally in `MessageConsumer` (not as middleware)
**Considered.** Applying a per-message deadline directly in `MessageConsumer.ConsumeAsync` would affect all messages without requiring middleware configuration. However:
- It violates the middleware pipeline design: all cross-cutting concerns should be expressible as `IMessageMiddleware`.
- It removes the ability to configure timeout per subscription or pipeline configuration.

## Consequences

- **Positive**: Eliminates indefinite thread pool occupation by misbehaving handlers.
- **Positive**: Correct cooperative cancellation — works with `await`-friendly handlers without any handler code changes.
- **Positive**: `TimeProvider`-injectable for deterministic tests using `FakeTimeProvider`.
- **Positive**: Non-breaking additive change — zero impact on consumers not using `AddHandlerTimeout()`.
- **Positive**: No new NuGet dependencies.
- **Trade-off**: Requires handlers to respect `CancellationToken`. Handlers that ignore the token may not terminate promptly even when the timeout fires. This is expected behavior under the cooperative cancellation contract of .NET async.

## References
- MassTransit `UseExecuteTimeout()` / Wolverine `ExecuteTimeout` — handler execution time limits
- .NET `CancellationTokenSource(TimeSpan, TimeProvider)` — .NET 8+ API used for testable timeouts
- `HandlerTimeoutMiddleware.cs` — `src/EricksonLopez.Messaging/Middleware/HandlerTimeoutMiddleware.cs`
- `HandlerTimeoutOptions.cs` — `src/EricksonLopez.Messaging/Middleware/HandlerTimeoutOptions.cs`
- Functional parity audit GAP-09
