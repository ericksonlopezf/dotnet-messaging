# ADR-016: Non-Breaking Delayed Redelivery and Message Deferral via IDeferableMessageTransport

## Context
Standard message brokers (such as Azure Service Bus and RabbitMQ with delayed message plugins) support scheduled/deferred message delivery. In distributed messaging, when a message processing failure is transient but requires a prolonged cool-down period (minutes or hours), in-process retry (`RetryMiddleware`) would hold connection threads, block semaphore capacity, and waste memory. Returning the message to the broker with a delayed delivery schedule avoids consumer thread starvation and enables resilient backoff across distributed worker nodes.

Modifying `IMessageTransport` directly to add `DeferRawAsync` would be a breaking change for existing custom transport implementations.

## Decision
1. Introduce an opt-in specialization interface `IDeferableMessageTransport` extending `IMessageTransport`:
   ```csharp
   namespace EricksonLopez.Messaging.Transport;

   public interface IDeferableMessageTransport : IMessageTransport
   {
       ValueTask<Result> DeferRawAsync(
           string destination,
           ReadOnlyMemory<byte> payload,
           MessageMetadata metadata,
           TimeSpan delay,
           CancellationToken cancellationToken = default);
   }
   ```
2. Implement `IDeferableMessageTransport` natively in:
   - `AzureServiceBusMessageTransport` using `ServiceBusMessage.ScheduledEnqueueTime`
   - `InMemoryMessageTransport` using background delayed dispatch backed by `TimeProvider`
3. If a transport does not implement `IDeferableMessageTransport`, pipelines or callers can gracefully detect support via runtime type inspection (`transport is IDeferableMessageTransport`) or fallback to standard dead-lettering/nacking.

## Consequences
- **Non-Breaking Evolution:** Existing `IMessageTransport` implementations remain 100% binary and source compatible.
- **Native Broker Scheduling:** Brokers with native scheduling capabilities (e.g., Azure Service Bus) schedule messages directly without external cron dependencies.
- **Testability:** `InMemoryMessageTransport` supports deterministic delayed delivery testing using injected `TimeProvider`.
