# ADR-003: Boundary Between Messaging and Mediator

## Context
Developers frequently conflate the Mediator pattern with Messaging buses, leading to hybrid monoliths where commands and queries are unnecessarily queued over brokers.

## Decision
- `EricksonLopez.Mediator`: Manages in-memory CQRS requests (`ICommand<TResponse>`, `IQuery<TResponse>`, `INotification`) returning immediate synchronous/asynchronous results without network serialization.
- `EricksonLopez.Messaging`: Manages asynchronous point-to-point and pub/sub message streams over brokers (`PublishAsync`, `SendAsync`).
- `Messaging` will not support in-memory request-response or query handling.

## Consequences
- Single-purpose libraries adhering strictly to the Single Responsibility Principle (SRP).
