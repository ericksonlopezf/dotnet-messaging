# Changelog

All notable changes to the `EricksonLopez.Messaging` ecosystem will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-08-28

### Added
- **Batch Publishing & Transport Optimization**:
  - `IBatchMessageTransport`: Opt-in interface for bulk publish operations with native transport-level batching.
  - `IMessagePublisher.PublishBatchAsync<T>` & `SendBatchAsync<T>`: High-throughput batch publishing APIs with transparent single-message fallback.
  - Implemented `IBatchMessageTransport` in `AzureServiceBusMessageTransport` using `ServiceBusMessageBatch` memory-safe chunking.
  - Documented decision in [ADR-021: Batch Publishing and Transport Optimization](docs/adr/adr-021-batch-publishing-and-transport-optimization.md).
- **Schema Versioning & Message Upcasting**:
  - `IMessageUpcaster`: Interface for versioned contract migrations (`CanUpcast`, `UpcastAsync`).
  - `MessageUpcastingMiddleware`: Pipeline middleware executing before handler dispatch to transform legacy schemas.
  - Added `AddMessageUpcaster<TUpcaster>()` and `AddUpcasting()` builder extensions.
  - Documented decision in [ADR-023: Schema Versioning and Message Upcasting](docs/adr/adr-023-schema-versioning-and-message-upcasting.md).
- **Native AOT & Source Generator Enhancements**:
  - `MessagingIncrementalGenerator` automatically emits `GeneratedMessagingJsonSerializerContext` pre-configured with all compile-time discovered `IMessage` types.
  - Seamless zero-reflection JSON serialization avoiding `IL2026` / `IL3050` trimming warnings.
  - Documented decision in [ADR-022: Source Generated JSON Serializer Context](docs/adr/adr-022-source-generated-json-serializer-context.md).
- **Cloud & Message Broker Transports**:
  - `EricksonLopez.Messaging.AwsSqs`: Production-ready AWS SQS message transport with long polling, FIFO grouping/deduplication, and DI extensions.
  - `EricksonLopez.Messaging.Kafka`: High-throughput Apache Kafka transport with header propagation, partition key routing, auto/manual commit modes, and DI extensions.
  - `EricksonLopez.Messaging.AzureServiceBus`: Enterprise Azure Service Bus transport with Connection String or Managed Identity (`TokenCredential` / `DefaultAzureCredential`) and scheduled delivery.
  - `EricksonLopez.Messaging.RabbitMQ`: AMQP-based messaging with connection recovery, publisher confirms, and dead-lettering.
  - `InMemoryMessageTransport`: High-throughput `System.Threading.Channels` in-memory transport with backpressure support (`ChannelCapacity`, `FullMode`). Implements `IMessageTransport`, `IDeferableMessageTransport` (using `Task.Delay` and `TimeProvider`), and `IBatchMessageTransport`.
- **Testing Utilities**:
  - `EricksonLopez.Messaging.Testing`: Standalone NuGet package featuring `InMemoryTestHarness`, `PublishedMessageList`, `ConsumedMessageList`, and asynchronous wait helpers (`WaitUntilPublishedAsync`).
- **Resiliency Middlewares**:
  - `CircuitBreakerMiddleware` & `CircuitBreakerOptions`: High-performance circuit breaker state machine (`Closed`, `Open`, `HalfOpen`) with `TimeProvider` support for fast-fail protection ([ADR-018](docs/adr/adr-018-circuit-breaker-middleware.md)).
  - `HandlerTimeoutMiddleware` & `HandlerTimeoutOptions`: Maximum execution duration limiter using linked cancellation tokens ([ADR-019](docs/adr/adr-019-handler-timeout-middleware.md)).
  - `RetryMiddleware`: Exponential backoff with jitter and injectable `TimeProvider`.
  - Added `AddCircuitBreaker` and `AddHandlerTimeout` builder extensions to `MessagingOptionsBuilder`.
- **Roslyn Diagnostic Analyzers**:
  - `ELMSG010` (`HandlerMustReturnResultAnalyzer`): Enforces that `IMessageHandler<T>.HandleAsync` returns `ValueTask<Result>`.
  - `ELMSG004` (`InvalidHandlerLifetimeAnalyzer`): Enforces that message handlers are registered only as `Scoped` services, flagging `AddSingleton` and `AddTransient` misuse.
  - `ELMSG002` (`MessageTypeAttributeAnalyzer`): Ensures all `IMessage` contracts declare `[MessageType("...")]`.
  - `ELMSG005` (`DomainBoundaryAnalyzer`): Prohibits exposing Domain entities directly on message contracts.
  - `ELMSG008` (`HandlerAsyncAnalyzer`): Prohibits synchronous blocking (`.Result`, `.Wait()`, `.GetAwaiter().GetResult()`) inside message handlers.
  - Documented in [ADR-020: ELMSG004 and ELMSG010 Diagnostic Analyzers](docs/adr/adr-020-elmsg004-elmsg010-analyzers.md).
- **Scheduled Delivery & Message Deferral**:
  - `IDeferableMessageTransport`: Non-breaking opt-in interface for delayed redelivery and broker scheduling ([ADR-016](docs/adr/adr-016-delayed-redelivery-and-message-deferral.md)).
- **Observability**:
  - `EricksonLopez.Messaging.OpenTelemetry`: W3C Trace Context propagation, activity enrichment, and messaging metrics (`MessagesPublished`, `MessagesReceived`, `MessagesFailed`, `ProcessingDuration`).
- **Architectural Documentation**:
  - Comprehensive `README.md` and `docs/architecture.md`.
  - Complete ecosystem integration guide in `docs/ecosystem-integration.md`.
  - Full set of Architectural Decision Records (`ADR-001` through `ADR-024`).
- **`MessagePublishOptions` and `MessageSendOptions` as Mutable Sealed Classes**:
  - Established that publish/send option objects are intentionally mutable `sealed class` types using object initializer syntax (not immutable records), documented in [ADR-024](docs/adr/adr-024-publish-options-mutable-class-vs-record.md).

### Changed
- Decoupled `EricksonLopez.Messaging.Abstractions` from local project references in favor of the published `EricksonLopez.Result` NuGet package.
- Consolidated solution structure in `EricksonLopez.Messaging.slnx`.
