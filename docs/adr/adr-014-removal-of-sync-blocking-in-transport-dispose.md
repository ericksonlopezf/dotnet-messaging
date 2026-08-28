# ADR-014: Removal of Sync-over-Async Blocking in Transport Disposal

## Context
Calling `DisposeAsync().AsTask().GetAwaiter().GetResult()` or `.Wait()` inside synchronous `IDisposable.Dispose()` methods leads to sync-over-async thread pool thread starvation, deadlocks in synchronization contexts, and unpredictable shutdown latency.

## Decision
- Sync-over-async blocking in `IDisposable.Dispose()` is eliminated across all transport implementations (`RabbitMqMessageTransport`, `AzureServiceBusMessageTransport`).
- Synchronous `Dispose()` performs immediate non-blocking disposal of client connections/channels or schedules detached async disposal without waiting.
- Applications and hosted services must favor `IAsyncDisposable.DisposeAsync()` for graceful asynchronous drain and teardown.

## Consequences
- Eliminates thread pool thread starvation during shutdown.
- Ensures graceful shutdown path is truly asynchronous and resilient.
