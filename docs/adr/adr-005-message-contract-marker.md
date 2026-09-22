# ADR-005: Message Marker Interface Contract

## Status
Accepted

## Date
2026-09-04

## Context
Message contracts need compile-time identification for Roslyn analyzers and source generators without forcing restrictive inheritance hierarchies.

## Decision
- Use `public interface IMessage;` as a lightweight marker interface.
- Generic constraints use `where TMessage : notnull`.
- Messages are explicitly assigned a stable type identifier via `[MessageType("...")]`.

## Consequences
- Record structs and record classes can implement `IMessage` without sacrificing domain inheritance.
- Analyzers can immediately detect invalid message designs.
