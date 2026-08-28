# ADR-011: Rejection of In-Core Persistence and Storage Bindings

## Context
Adding database drivers, EF Core bindings, or message store persistence directly into `EricksonLopez.Messaging` blurs architectural boundaries, introduces heavyweight external dependencies, complicates Native AOT compilation, and violates Single Responsibility Principle.

## Decision
- Message storage, persistence, transactional outbox tables, and idempotency store implementations are **REJECTED** from the `EricksonLopez.Messaging` core.
- Outbox and deduplication persistence reside in dedicated specialized packages (`EricksonLopez.Outbox`, `EricksonLopez.Messaging.Storage.*`).
- Core messaging remains an in-memory & distributed transport abstraction only.

## Consequences
- Core package remains ultra-lightweight and dependency-minimal.
- Seamless compatibility with Native AOT and arbitrary persistence engines.
- Clear separation of concerns in modular ecosystem.
