# Architectural Decision Record: REJECT-008

## Status
Rejected

## Date
2026-09-04

## Rejection of Broker-Specific Leaks in Messaging Abstractions

### Status
**REJECTED (Permanent Directorial Invariant)**

### Context
Proposals suggested exposing RabbitMQ exchange types, Kafka partition offsets, or Azure Service Bus lock tokens directly inside `IMessagePublisher` or `IMessageHandler<T>`.

### Decision
Permanently rejected. `EricksonLopez.Messaging.Abstractions` maintains a pure, transport-agnostic publish/subscribe and point-to-point contract. Provider-specific features are encapsulated within dedicated provider packages (`Messaging.RabbitMQ`, `Messaging.AzureServiceBus`).

### Consequences
- Pluggable infrastructure: Handlers remain independent of the underlying message broker.
- Clean Architecture and DDD principles strictly preserved.
