# ADR-002: Boundary Between Messaging and Events Abstractions

## Status
Accepted

## Context
In event-driven and distributed .NET architectures, confusion often arises regarding the distinction and responsibility boundaries between `EricksonLopez.Events.Contracts` (`IEventPublisher`, `IEventBus`) and `EricksonLopez.Messaging.Abstractions` (`IMessagePublisher`).

Developers and library consumers require a clear, unambiguous rule to know when to use each abstraction, preventing redundant API conflation, contract merging, or inappropriate infrastructure leakage into domain and application layers.

## Decision

### 1. Distinct Package Ownership & Modularity
* **Do NOT merge the contracts**: `EricksonLopez.Events.Contracts` and `EricksonLopez.Messaging.Abstractions` shall remain distinct packages with orthogonal responsibilities.
* **`dotnet-events` (`EricksonLopez.Events.Contracts`)**: Defines event semantics independent of underlying network transport. Represents facts and state changes that have occurred in the domain.
* **`dotnet-messaging` (`EricksonLopez.Messaging.Abstractions`)**: Defines generic message transport and routing abstractions for communicating across physical network brokers.

### 2. Core Usage Heuristic
* **"Something Has Happened" (`IEventPublisher`)**:
  * Used when expressing domain facts or integration events (e.g., `OrderCreated`, `CustomerRegistered`, `PaymentCompleted`).
  * Preferred abstraction in Domain and Application layers.
  * An Application Service should never need to know a physical queue, broker topic, or routing key just to publish a domain event.
* **"Send/Publish this Message to Destination Y" (`IMessagePublisher`)**:
  * Used when expressing commands, generic payloads, or when physical routing/destination concerns are explicit (e.g., `CreateOrderCommand`, `SendNotificationMessage`, `PublishToQueue(...)`).
  * Infrastructure and integration components that control destinations, queues, topics, or transport execution depend on `IMessagePublisher`.
  * Returns `ValueTask<Result>` to explicitly represent physical transport outcomes and broker acknowledgements without exceptions.

### 3. Responsibility Matrix: `IEventPublisher` vs. `IEventBus` vs. `IMessagePublisher`

| Contract | Package | Architectural Layer | Primary Responsibility | Error Handling | Destination Concern |
|---|---|---|---|---|---|
| `IEventPublisher` | `Events.Contracts` | Domain / Application | Publishes `IEvent` instances to registered handlers. | Functional / Exceptions | None (Implicit by type) |
| `IEventBus` | `Events.Contracts` | Application / Infrastructure | In-process mediator/bus orchestrator (inherits `IEventPublisher`). | Pipeline orchestration | None (In-process routing) |
| `IMessagePublisher` | `Messaging.Abstractions` | Infrastructure / Transport | Dispatches generic messages and commands across network brokers. | `ValueTask<Result>` | Explicit or configurable destination |

### 4. Infrastructure Composition
Infrastructure adapters may implement `IEventPublisher` on top of `IMessagePublisher` (or a transactional outbox) to relay integration events to message brokers without conflating the domain-level event contract with the transport contract.

## Consequences
* **Positive**: Domain and Application layers remain purely semantic and decoupled from physical messaging topology (queues, topics, exchange names).
* **Positive**: Infrastructure adapters retain fine-grained control over routing, destinations, and transport result handling.
* **Governance Rule (AO-005)**: Neither package may absorb or duplicate the responsibility of the other.
