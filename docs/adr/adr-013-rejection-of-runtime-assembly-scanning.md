# ADR-013: Rejection of Runtime Assembly Scanning in Core DI

## Status
Rejected

## Date
2026-09-04

## Context
Traditional .NET frameworks discover handlers by scanning `AppDomain.CurrentDomain.GetAssemblies()` and searching for types implementing `IMessageHandler<T>`. In Native AOT, untrimmed assemblies are not loaded in the AppDomain, unreferenced types are aggressively trimmed by the ILLink tool, and runtime reflection causes trimming warnings (`IL2026`, `IL3050`).

## Decision
- Runtime dynamic assembly scanning is **REJECTED** from the `EricksonLopez.Messaging` core runtime path.
- Handler registration uses explicit type parameters (`services.AddMessageHandler<TMessage, THandler>()`) annotated with `[DynamicallyAccessedMembers]` or Roslyn Source Generators (`EricksonLopez.Messaging.Generators`).

## Consequences
- 100% Native AOT trimming safe with zero linker warnings.
- Instant cold-start startup without CPU/reflection reflection penalty.
- Predictable and explicit dependency graph.
