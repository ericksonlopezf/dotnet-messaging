# ADR-007: Source-Generated Zero-Reflection Dispatch

## Context
Reflection scanning at runtime (e.g. `Assembly.GetTypes()` or `Activator.CreateInstance()`) causes high startup latency, significant heap allocations, and breaks Native AOT compilation and assembly trimming.

## Decision
- Implement Roslyn Incremental Generators (`EricksonLopez.Messaging.Generators`) to discover `IMessageHandler<T>` at compile time.
- Emit precompiled static dispatch delegates and DI registration methods.
- Use `ConcurrentDictionary<string, HandlerBinding>` for O(1) fast-path execution with thread-safe handler registration during startup.

> **Implementation Note**: An earlier version of this ADR proposed `FrozenDictionary<string, HandlerInvoker>`.
> The current implementation uses `ConcurrentDictionary<string, HandlerBinding>`, which allows dynamic handler registration
> during startup while maintaining O(1) amortized lookup on the hot path. A future optimization could freeze the dictionary
> after the DI container is built if startup mutation is no longer required.

## Consequences
- 100% Native AOT certified.
- Zero boxing on dispatch fast paths.
- Handler registration is thread-safe during host startup via `ConcurrentDictionary`.
