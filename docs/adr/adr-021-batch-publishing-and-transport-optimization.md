# ADR-021: Batch Publishing and Transport Optimization via IBatchMessageTransport

## Status
Accepted — August 2026

## Context
High-throughput distributed systems frequently produce messages in batches (e.g., bulk order creation, periodic synchronization events, data ingestion pipelines). Publishing messages one-by-one introduces:

1. **Network roundtrip overhead**: Each `PublishAsync` sends an independent HTTP/AMQP/gRPC request to the broker.
2. **Broker lock contention**: Sequential publishing limits throughput compared to native broker batch APIs (such as Azure Service Bus `ServiceBusSender.SendMessagesAsync` or SQS `SendMessageBatchAsync`).
3. **Trace fragmentation**: Individual spans are emitted per message rather than representing the cohesive batch operation.

Modifying `IMessageTransport` directly to add batch publishing would be a breaking change for existing custom transport implementations.

## Decision

1. Extend `IMessagePublisher` with batch publishing overloads:
   ```csharp
   ValueTask<Result> PublishBatchAsync<TMessage>(
       IEnumerable<TMessage> messages,
       MessagePublishOptions? options = null,
       CancellationToken cancellationToken = default) where TMessage : notnull;

   ValueTask<Result> SendBatchAsync<TMessage>(
       IEnumerable<TMessage> messages,
       string destination,
       MessageSendOptions? options = null,
       CancellationToken cancellationToken = default) where TMessage : notnull;
   ```

2. Introduce an opt-in specialization interface `IBatchMessageTransport` extending `IMessageTransport`:
   ```csharp
   namespace EricksonLopez.Messaging.Transport;

   public interface IBatchMessageTransport : IMessageTransport
   {
       ValueTask<Result> PublishBatchRawAsync(
           string destination,
           IReadOnlyList<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)> batch,
           CancellationToken cancellationToken = default);
   }
   ```

3. Implement `IBatchMessageTransport` in:
   - `AzureServiceBusMessageTransport`: Using `sender.SendMessagesAsync(IReadOnlyList<ServiceBusMessage>)` in a single broker roundtrip.
   - `InMemoryMessageTransport`: Atomic enqueue to all registered subscriber channels.

4. Provide transparent fallback in `MessagePublisher`: If the configured transport does not implement `IBatchMessageTransport`, `MessagePublisher` automatically iterates over the serialized batch and publishes each item sequentially via `PublishRawAsync`, returning the first encountered failure (if any).

## Consequences
- **High Throughput**: Dramatically lowers latency and network I/O when publishing multiple messages.
- **Non-Breaking Evolution**: Existing `IMessageTransport` implementations remain 100% binary and source compatible.
- **Observability**: `MessagePublisher` records a single `publish_batch` or `send_batch` Activity with the `messaging.batch.count` tag while incrementing `MessagingDiagnostics.MessagesPublished` by the batch size.
