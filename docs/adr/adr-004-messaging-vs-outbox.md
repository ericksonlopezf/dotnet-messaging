# ADR-004: Dependency Direction: Outbox to Messaging

## Context
Guaranteed at-least-once message delivery requires persisting messages within the database transaction prior to broker publication.

## Decision
- Dependency direction: **`EricksonLopez.Outbox` $\rightarrow$ `EricksonLopez.Messaging`**.
- `Outbox` acts as the persistence and background dispatch engine. When sending pending messages, the Outbox background poller delegates to `IMessageTransport` or `IMessagePublisher` in `Messaging`.
- `Messaging` contains zero database schemas, SQL queries, or poller background services.

## Consequences
- Messaging remains completely pure, database-agnostic, and lightweight.
- Outbox can target any broker adapter through `IMessageTransport`.
