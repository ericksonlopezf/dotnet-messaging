# Troubleshooting & FAQ — EricksonLopez.Messaging

Frequently asked questions, common error codes, and troubleshooting solutions for `EricksonLopez.Messaging`.

---

## 1. Common Error Codes & Diagnostics

### `Messaging.HandlerNotFound`
- **Cause**: The received message contains a `MessageType` for which no `IMessageHandler<T>` has been registered in the DI container.
- **Resolution**: Ensure `services.AddMessageHandler<TMessage, THandler>()` or `services.AddGeneratedMessagingHandlers()` has been invoked in `Program.cs`, and verify that the `[MessageType("...")]` string on the producer matches the consumer contract exactly.

---

### `Messaging.CircuitBreaker.Open`
- **Cause**: The consumer handler has accumulated consecutive failures exceeding `FailureThreshold` in `CircuitBreakerOptions`.
- **Resolution**:
  - Check application logs to diagnose downstream dependency outages.
  - The Circuit Breaker will automatically transition to `HalfOpen` after `BreakDuration` and recover upon the first successful execution.

---

### `Messaging.Handler.Timeout`
- **Cause**: Handler execution time exceeded `HandlerTimeoutOptions`.
- **Resolution**:
  - Adjust timeout duration with `options.AddHandlerTimeout(TimeSpan.FromSeconds(30))`.
  - Ensure all asynchronous calls inside `HandleAsync` observe the supplied `CancellationToken`.

---

### `Messaging.DeserializationFailed`
- **Cause**: The incoming JSON payload cannot be deserialized into the target message contract, or the message type is missing from the Native AOT `JsonSerializerContext`.
- **Resolution**:
  - If publishing with Native AOT, ensure `EricksonLopez.Messaging.Generators` is referenced.
  - Check that all properties have valid public getters and setters/init accessors supported by `System.Text.Json`.

---

## 2. Roslyn Analyzer Rules Reference

| Rule ID | Severity | Cause | Resolution |
| :--- | :---: | :--- | :--- |
| **`ELMSG002`** | `Error` | Message type missing `[MessageType]` attribute | Add `[MessageType("unique.name.v1")]` to the `IMessage` contract. |
| **`ELMSG004`** | `Error` | Handler registered with invalid lifetime (Singleton/Transient) | Always register handlers with `Scoped` lifetime (`AddMessageHandler`). |
| **`ELMSG005`** | `Error` | Message contract references Domain entity types | Use primitive types, records, or DTOs exclusively. |
| **`ELMSG008`** | `Error` | Synchronous blocking (`.Result`, `.Wait()`) in handler | Replace blocking calls with `await`. |
| **`ELMSG010`** | `Error` | `HandleAsync` does not return `ValueTask<Result>` | Change method return type to `ValueTask<Result>`. |

---

## 3. Frequently Asked Questions (FAQ)

### Why do handlers return `ValueTask<Result>` instead of throwing exceptions?
Exceptions incur significant CPU overhead when capturing stack traces and make control flow opaque. Returning `Result` makes validation and business errors explicit, type-safe, and deterministic.

### How do I unit test message handlers?
Use `EricksonLopez.Messaging.Testing` and `InMemoryTestHarness`. It allows you to publish test messages and query `harness.PublishedMessages` and `harness.ConsumedMessages` in-memory without Docker.

### Can I use multiple message brokers simultaneously?
`IMessageTransport` is registered as a singleton for the application's active transport. To integrate multiple distinct message brokers in the same solution, configure separate bounded context host modules.
