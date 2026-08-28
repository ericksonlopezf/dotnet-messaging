# ADR-001: Definition and Scope of Messaging in the Ecosystem

## Context
The EricksonLopez ecosystem provides dedicated libraries for in-process mediator (`EricksonLopez.Mediator`), in-process event bus (`EricksonLopez.EventBus`), and transactional outbox (`EricksonLopez.Outbox`). A clear definition of `EricksonLopez.Messaging` is required to prevent scope creep and duplicate abstractions.

## Decision
1. `EricksonLopez.Messaging` is defined strictly as an **asynchronous, distributed, out-of-process messaging framework**.
2. It bridges bounded context boundaries over message broker transports (RabbitMQ, Kafka, Azure Service Bus, AWS SQS, NATS).
3. In-process messaging is limited to an `InMemoryMessageTransport` (`System.Threading.Channels`) intended for modular monoliths and integration testing.

## Consequences
- Clean separation between local in-process CQRS/events and distributed network messaging.
- Core package contains zero database tables, SQL scripts, or ORM dependencies.
