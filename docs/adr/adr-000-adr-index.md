# Architectural Decision Records — Index

## Status
Accepted

## Date
2026-09-04

This document is a navigable index of all Architectural Decision Records (ADRs) and Permanent Directorial Invariants (REJECT records) for the `EricksonLopez.Messaging` ecosystem.

ADRs capture the context, rationale, and consequences of significant design decisions made in this project. They serve as the authoritative reference for understanding _why_ the system is designed the way it is.

---

## Decision Records

| ADR | Title | Status | Summary |
| :--- | :--- | :---: | :--- |
| [ADR-001](adr-001-definition-and-scope.md) | Definition and Scope of Messaging in the Ecosystem | Accepted | `EricksonLopez.Messaging` is strictly an asynchronous, distributed, out-of-process messaging framework. In-process messaging is limited to `InMemoryMessageTransport` for testing and modular monoliths. |
| [ADR-002](adr-002-messaging-vs-eventbus.md) | Boundary Between Messaging and Events Abstractions | Accepted | `EricksonLopez.Events` handles in-process domain events; `EricksonLopez.Messaging` handles distributed cross-boundary messages. `EricksonLopez.Messaging.Events` provides an `IEventPublisher` bridge between the two. |
| [ADR-003](adr-003-messaging-vs-mediator.md) | Boundary Between Messaging and Mediator | Accepted | `EricksonLopez.Mediator` owns in-process CQRS commands and queries. `EricksonLopez.Messaging` owns distributed pub/sub and work queues. These are separate concerns and must not be conflated. |
| [ADR-004](adr-004-messaging-vs-outbox.md) | Dependency Direction: Outbox to Messaging | Accepted | `EricksonLopez.Outbox` depends on `EricksonLopez.Messaging.Abstractions` (not the reverse). The Outbox relay publishes messages via `IMessagePublisher` to guarantee at-least-once delivery. |
| [ADR-005](adr-005-message-contract-marker.md) | Message Marker Interface Contract | Accepted | `IMessage` is a pure marker interface. Message contracts are immutable C# records decorated with `[MessageType("...")]` for AOT dispatch and Roslyn analyzer discovery. |
| [ADR-006](adr-006-envelope-and-metadata.md) | MessageEnvelope and Metadata Strategy | Accepted | `MessageEnvelope<T>` pairs a strongly-typed payload with its `MessageContext`. `TransportMessageMetadata` carries network routing, timestamps, and headers without polluting domain models. |
| [ADR-007](adr-007-source-generated-dispatch.md) | Source-Generated Zero-Reflection Dispatch | Accepted | Runtime reflection for handler discovery is rejected. `MessagingIncrementalGenerator` emits compile-time strongly-typed dispatch delegates and `JsonSerializerContext` registrations for Native AOT compatibility. |
| [ADR-008](adr-008-result-pattern-integration.md) | Result Pattern Integration in Handlers | Accepted | Handlers return `ValueTask<Result>` (`EricksonLopez.Result`) instead of throwing exceptions for expected failures, eliminating exception-driven control flow overhead. Enforced by `ELMSG010`. |
| [ADR-009](adr-009-transport-abstraction.md) | Minimalist Byte-Oriented Transport Abstraction | Accepted | `IMessageTransport` is a thin, byte-oriented contract (`ReadOnlyMemory<byte>`) rather than a universal broker abstraction, avoiding leaky cross-broker concept unification. |
| [ADR-010](adr-010-rejection-of-in-core-rpc.md) | Rejection of Request/Response RPC in Messaging Core | Rejected | Synchronous RPC over message queues introduces temporal coupling, overhead, and anti-patterns. Use `EricksonLopez.Mediator` for in-process request/response instead. |
| [ADR-011](adr-011-rejection-of-in-core-persistence.md) | Rejection of In-Core Persistence and Storage Bindings | Rejected | Database drivers, EF Core bindings, and outbox tables are excluded from `EricksonLopez.Messaging`. Persistence concerns belong in `EricksonLopez.Outbox`. |
| [ADR-012](adr-012-rejection-of-in-core-sagas.md) | Rejection of In-Core Saga State Machine Orchestration | Rejected | Complex saga orchestration with durable storage and timers is excluded from core. Choreographed sagas use standard message handlers. |
| [ADR-013](adr-013-rejection-of-runtime-assembly-scanning.md) | Rejection of Runtime Assembly Scanning in Core DI | Rejected | `AppDomain.GetAssemblies()` scanning is rejected for AOT/trim compatibility. Explicit `AddMessageHandler<T, H>()` or source generators are required. |
| [ADR-014](adr-014-removal-of-sync-blocking-in-transport-dispose.md) | Removal of Sync-over-Async Blocking in Transport Disposal | Accepted | `.GetAwaiter().GetResult()` and `.Wait()` in `IDisposable.Dispose()` are eliminated to prevent ThreadPool starvation during host shutdown. |
| [ADR-015](adr-015-test-naming-osherove-ide1006.md) | Institutionalization of Osherove Test Naming Pattern | Accepted | Test methods use the `[UnitOfWork]_[StateUnderTest]_[ExpectedBehavior]` convention. `IDE1006`/`CA1707` are locally suppressed in test projects via `.editorconfig` or `#pragma`. |
| [ADR-016](adr-016-delayed-redelivery-and-message-deferral.md) | Non-Breaking Delayed Redelivery via `IDeferableMessageTransport` | Accepted | `IDeferableMessageTransport` is an opt-in interface extending `IMessageTransport` for delayed/scheduled delivery without breaking existing custom transport implementations. |
| [ADR-017](adr-017-messagemetadata-transport-boundary-vs-outbox.md) | `TransportMessageMetadata` Semantic Boundary vs Outbox Storage Metadata | Accepted | `TransportMessageMetadata` in `EricksonLopez.Messaging.Abstractions` carries network-layer transport headers only. Outbox persistence metadata (`EricksonLopez.Outbox`) is a distinct, separate type. |
| [ADR-018](adr-018-circuit-breaker-middleware.md) | Circuit Breaker Middleware for Consumer Pipeline Resilience | Accepted | `CircuitBreakerMiddleware` implements a fast-fail state machine (`Closed`, `Open`, `HalfOpen`) with `TimeProvider` support to protect consumers from cascading downstream failures. |
| [ADR-019](adr-019-handler-timeout-middleware.md) | Handler Timeout Middleware via Linked CancellationToken | Accepted | `HandlerTimeoutMiddleware` enforces maximum handler execution time using linked cancellation tokens, preventing hung handlers from occupying consumer semaphore slots indefinitely. |
| [ADR-020](adr-020-elmsg004-elmsg010-analyzers.md) | Roslyn Analyzers ELMSG004 and ELMSG010 | Accepted | `ELMSG004` enforces `Scoped` handler lifetime registration; `ELMSG010` enforces `ValueTask<Result>` return type on `HandleAsync`. Both were previously defined but unimplemented; now enforced by dedicated `DiagnosticAnalyzer` classes. |
| [ADR-021](adr-021-batch-publishing-and-transport-optimization.md) | Batch Publishing and Transport Optimization via `IBatchMessageTransport` | Accepted | `IBatchMessageTransport` is an opt-in interface for high-throughput bulk publishing with native transport-level batching (e.g. `ServiceBusMessageBatch`). Falls back to individual publishing when not implemented. |
| [ADR-022](adr-022-source-generated-json-serializer-context.md) | Source-Generated `JsonSerializerContext` for Zero-Reflection Native AOT | Accepted | `MessagingIncrementalGenerator` automatically emits `GeneratedMessagingJsonSerializerContext` pre-registered with all discovered `IMessage` types, avoiding `IL2026`/`IL3050` trimming warnings. |
| [ADR-023](adr-023-schema-versioning-and-message-upcasting.md) | Message Schema Versioning and Upcasting Pipeline | Accepted | `IMessageUpcaster<in TOld, out TNew>` and `MessageUpcastingMiddleware` provide transparent migration of legacy message schemas before handler dispatch, decoupling contract evolution from consumer upgrade timelines. |
| [ADR-024](adr-024-publish-options-mutable-class-vs-record.md) | MessagePublishOptions and MessageSendOptions as Mutable Sealed Classes | Accepted | `MessagePublishOptions` and `MessageSendOptions` are mutable `sealed class` types using object initializer syntax, not immutable positional records. Documents the rationale for mutable configuration objects and the `Destination` property naming. |
| [ADR-025](adr-025-monotargeting-dotnet-10.md) | Monotargeting .NET 10 for Production Libraries | Accepted | Production runtime packages target `.NET 10 (net10.0)` exclusively to maximize RyuJIT AVX-512 vectorization and Native AOT zero-allocation primitives without conditional multi-targeting overhead. Compiler tools target `.NET Standard 2.0`. |

---

## Permanent Directorial Invariants (REJECT Records)

These records document architectural directions that were **explicitly evaluated and permanently rejected**. They serve as authoritative guidance to prevent the same proposals from being re-introduced.

| Record | Title | Rationale Summary |
| :--- | :--- | :--- |
| [REJECT-008](reject-008-broker-specific-abstractions-in-messaging-contracts.md) | Rejection of Broker-Specific Leaks in Messaging Abstractions | Exposing RabbitMQ exchange types, Kafka partition offsets, or Azure Service Bus lock tokens in `IMessagePublisher` or `IMessageHandler<T>` would create a leaky abstraction that breaks broker-agnosticism. The transport layer handles these concerns entirely. |

---

## Quick Reference by Concern

| Concern | Relevant ADRs |
| :--- | :--- |
| **Scope & Boundaries** | ADR-001, ADR-002, ADR-003, ADR-004 |
| **Contracts & Immutability** | ADR-005, ADR-006, ADR-024 |
| **Native AOT & Zero-Reflection** | ADR-007, ADR-013, ADR-022 |
| **Functional Error Handling** | ADR-008 |
| **Transport Design** | ADR-009, ADR-016, ADR-021, REJECT-008 |
| **Explicit Rejections** | ADR-010, ADR-011, ADR-012, ADR-013 |
| **Resilience & Middleware** | ADR-018, ADR-019 |
| **Observability & Dispatch** | ADR-007, ADR-022 |
| **Schema Evolution** | ADR-023 |
| **Lifecycle & DI** | ADR-020 |
| **Analyzers & Enforcement** | ADR-020 |
| **Cross-System Architecture** | ADR-017, ADR-004 |
| **Testing & Code Style** | ADR-014, ADR-015 |