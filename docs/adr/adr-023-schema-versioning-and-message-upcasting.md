# ADR-023: Message Schema Versioning and Upcasting Pipeline

## Status
Accepted — August 2026

## Context
In evolving distributed architectures, message contracts inevitably change (new fields added, fields renamed, types restructured). Older service instances or stored event replays may emit previous versions of message schemas (e.g. `v1`), while newer handlers expect the modern contract (e.g. `v2`).

Without an explicit schema migration mechanism:
1. Handlers must maintain defensive conditional logic to support legacy payloads.
2. Breaking schema changes force lock-step deployments across all producers and consumers.

## Decision

1. Define `IMessageUpcaster<in TOldMessage, out TNewMessage>` in `EricksonLopez.Messaging.Abstractions.Contracts`:
   ```csharp
   public interface IMessageUpcaster<in TOldMessage, out TNewMessage>
       where TOldMessage : class, IMessage
       where TNewMessage : class, IMessage
   {
       TNewMessage Upcast(TOldMessage oldMessage, TransportMessageMetadata metadata);
   }
   ```

2. Introduce `MessageUpcastingMiddleware : IMessageMiddleware` in `EricksonLopez.Messaging.Middleware`:
   - Intercepts incoming messages before they reach the dispatcher/handler.
   - Matches the received message payload type against registered `IMessageUpcaster` instances.
   - Transforms legacy message instances to their target upgraded contract, mutating `MessageContext.Message` transparently.

3. Provide fluent DI registration:
   ```csharp
   services.AddMessageUpcaster<OrderCreatedV1, OrderCreatedV2, OrderCreatedUpcaster>();
   services.AddMessaging(options =>
   {
       options.AddUpcasting();
   });
   ```

## Consequences
- **Decoupled Evolution**: Handlers only need to implement logic for the current contract version.
- **Backward Compatibility**: Producers can continue publishing older contract versions during phased migrations.
- **Pipeline Interoperability**: Fits natively as a standard `IMessageMiddleware` without modifying the core `IMessageHandler<T>` contract.
