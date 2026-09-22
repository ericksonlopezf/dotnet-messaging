# Changelog

All notable changes to the `EricksonLopez.Messaging` ecosystem will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [2.0.0] - 2026-09-22

### Added
- **Message Deduplication & Idempotency Pipeline**:
  - `IMessageDeduplicationStore`: Storage contract for atomic idempotency lease acquisition and release (`TryAcquireAsync`, `ReleaseAsync`).
  - `InMemoryMessageDeduplicationStore`: Thread-safe in-memory deduplication provider with sliding expiration and `TimeProvider` support.
  - `MessageDeduplicationMiddleware` & `MessageDeduplicationOptions`: Pipeline middleware preventing duplicate message execution in at-least-once transport delivery.
  - Builder extension `opts.AddDeduplication(Action<MessageDeduplicationOptions>?)` on `MessagingOptionsBuilder`.
- **Partition Routing & Dynamic Key Resolution**:
  - `IPartitionKeyResolver`: Zero-reflection contract for resolving message partition keys dynamically at runtime without reflection.
  - `PartitionKeyInfo`: Compile-time model in `EricksonLopez.Messaging.Generators` for static partition key extraction.
- **Consumer Concurrency Tuning & Dispatch Enhancements**:
  - `MessageConsumerOptions`: Fine-grained configuration for `MaxConcurrency`, `PrefetchCount`, and `UnhandledFailureAckResult`.
  - `MessageDispatchItem`: Minimal-allocation struct encapsulating raw payload and transport metadata for batch dispatch.
  - `IHandlerRegistry`: Clean registry interface separating handler registration mechanics from the concrete dispatcher.
  - `PartitionOffsetTracker`: Thread-safe partition offset tracking utility in `EricksonLopez.Messaging.Kafka`.
- **Showcase Reference Implementation & Documentation**:
  - Updated showcase reference guide at `docs/showcase/showcase-guide.md` with complete level-by-level walkthroughs (L00–L11).
  - Standardized all technical architecture, functional maps, public API reference, and cookbook documentation in pure technical English.
  - Aligned all showcase levels to genuine library APIs (`IMessage`, `IMessageHandler<T>`, `MessageContext`, `ValueTask<Result>`).
- **OpenTelemetry Metrics Integration**:
  - Added `AddMessagingInstrumentation()` extension on `MeterProviderBuilder` in `EricksonLopez.Messaging.OpenTelemetry` for standard OpenTelemetry metrics registration.

### Breaking Changes
- **BC-001 (`IMessageUpcasterInvoker.TargetType`)**:
  - **Change**: Added property `Type TargetType { get; }` to the public interface `IMessageUpcasterInvoker`.
  - **Previous State**: Interface only exposed `Type SourceType { get; }` and `object Upcast(...)`.
  - **Current State**: Requires implementers to declare `Type TargetType { get; }` indicating the destination contract type.
  - **Affected Consumers**: Any custom classes implementing `IMessageUpcasterInvoker` directly (rather than using the built-in `MessageUpcasterInvoker<TOld, TNew, TUpcaster>`).
  - **Migration Guidance**: Add `public Type TargetType => typeof(TNewMessage);` to any custom `IMessageUpcasterInvoker` implementation or inherit from `MessageUpcasterInvoker<TOld, TNew, TUpcaster>`.
- **BC-002 (`DefaultMessageDispatcher` Constructor Signature & Binding Collection Type)**:
  - **Change**: Parameter `bindings` changed from `IDictionary<string, HandlerBinding>?` to `IDictionary<string, IReadOnlyList<HandlerBinding>>?`, and parameter `upcasters` was appended.
  - **Previous State**: Constructor accepted a dictionary with a single `HandlerBinding` per message type name.
  - **Current State**: Constructor requires a dictionary mapping message type names to an `IReadOnlyList<HandlerBinding>` to support multi-handler Pub/Sub dispatch. The 4-parameter constructor symbol was removed from the compiled assembly.
  - **Affected Consumers**: Any consumer manually instantiating `DefaultMessageDispatcher` instead of resolving it from Microsoft DI.
  - **Migration Guidance**: Recompile callers against the new assembly. Wrap single bindings in an array/list: `new Dictionary<string, IReadOnlyList<HandlerBinding>> { [key] = new[] { binding } }` or use `services.AddMessaging()`.
- **BC-003 (`RetryMiddleware` Constructor Signature Binary Change)**:
  - **Change**: The 3-parameter public constructor `.ctor(int, TimeSpan?, TimeProvider?)` was replaced in assembly IL by a 5-parameter constructor accepting optional `maxDelay` and `shouldRetry`.
  - **Previous State**: Precompiled assemblies referenced `.ctor(int, Nullable<TimeSpan>, TimeProvider)`.
  - **Current State**: Calling code compiled against v1.0.0 will throw `System.MissingMethodException` at runtime unless recompiled.
  - **Affected Consumers**: Precompiled third-party assemblies calling `new RetryMiddleware(maxRetries, initialDelay, timeProvider)` directly.
  - **Migration Guidance**: Recompile calling assemblies against 2.0.0, or use the options-based constructor overload `new RetryMiddleware(new RetryOptions { ... })`.
- **BC-004 (`MessagePublisher` Constructor Signature Binary Change)**:
  - **Change**: The 2-parameter public constructor `.ctor(IMessageTransport, IMessageSerializer)` was replaced in assembly IL by a 3-parameter constructor accepting `IEnumerable<IPartitionKeyResolver>?`.
  - **Previous State**: Precompiled assemblies referenced `.ctor(IMessageTransport, IMessageSerializer)`.
  - **Current State**: Precompiled binaries calling `new MessagePublisher(transport, serializer)` without recompiling will throw `System.MissingMethodException`.
  - **Affected Consumers**: Applications manually instantiating `MessagePublisher` without recompiling against the updated package.
  - **Migration Guidance**: Recompile client code against 2.0.0 or resolve `IMessagePublisher` from dependency injection via `services.AddMessaging()`.
- **BC-005 (`MessageConsumer` Constructor Signature Binary Change)**:
  - **Change**: The 6-parameter public constructor was replaced in assembly IL by a 7-parameter constructor accepting `IOptions<MessageConsumerOptions>?`.
  - **Previous State**: Precompiled assemblies referenced `.ctor(IMessageTransport, IMessageDispatcher, IServiceScopeFactory, IEnumerable<string>?, IEnumerable<IHandlerRegistration>?, ILogger<MessageConsumer>?)`.
  - **Current State**: Precompiled binaries calling the 6-parameter constructor without recompiling will throw `System.MissingMethodException`.
  - **Affected Consumers**: Code manually instantiating `MessageConsumer`.
  - **Migration Guidance**: Recompile client code against 2.0.0 or resolve `IMessageConsumer` via `services.AddMessaging()`.
- **BC-006 (`NativeAotJsonSerializer` Removal of Reflection Fallback)**:
  - **Change**: Removed `DefaultJsonTypeInfoResolver` from `CreateDefaultOptions()` to guarantee trim-safety and zero-reflection Native AOT execution.
  - **Previous State**: Arbitrary message classes without source-generated metadata could be serialized using reflection fallback in standard .NET runtimes.
  - **Current State**: Only contracts explicitly registered in a source-generated `JsonSerializerContext` (or provided via custom `IJsonTypeInfoResolver`) can be serialized/deserialized; unregistered contracts throw `NotSupportedException`.
  - **Affected Consumers**: Applications using `NativeAotJsonSerializer` without Roslyn source generation (`[JsonSerializable]`) or without referencing `EricksonLopez.Messaging.Generators`.
  - **Migration Guidance**: Register message contract types in a `JsonSerializerContext` using `[JsonSerializable(typeof(TMessage))]` or install `EricksonLopez.Messaging.Generators` to automatically discover and generate contexts at compile time.
- **BC-007 (`EricksonLopez.Messaging.Kafka` AOT Compatibility Disabled)**:
  - **Change**: Configured `<IsAotCompatible>false</IsAotCompatible>` on `EricksonLopez.Messaging.Kafka.csproj`.
  - **Previous State**: Inherited `<IsAotCompatible>true</IsAotCompatible>` globally from `Directory.Build.props`.
  - **Current State**: Applications compiling with `<PublishAot>true</PublishAot>` or `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` referencing `EricksonLopez.Messaging.Kafka` will receive analyzer warnings or build failures due to `librdkafka` C-interop.
  - **Affected Consumers**: Applications attempting to compile Kafka messaging clients into Native AOT single-file executables.
  - **Migration Guidance**: Exclude Kafka transport from Native AOT binaries or use Azure Service Bus, RabbitMQ, or AWS SQS transports for AOT-targeted services.
- **BC-008 (`MessageConsumer` Fault-Handling & Dead-Letter Routing)**:
  - **Change**: Failed message dispatches now route to `IDeadLetterQueue` (returning `TransportAckResult.DeadLetter`) rather than unconditionally returning `TransportAckResult.Ack`. Messages cancelled during host shutdown now return `TransportAckResult.NackRequeue`.
  - **Previous State**: Functional failures were acknowledged (`Ack`) and discarded regardless of DLQ registration; cancelled shutdown messages could be acknowledged.
  - **Current State**: Handlers returning `Result.Failure(...)` will trigger DLQ forwarding if `IDeadLetterQueue` is registered, or return `_options.UnhandledFailureAckResult`.
  - **Affected Consumers**: Applications relying on unhandled handler failures being silently dropped/acknowledged on the broker.
  - **Migration Guidance**: If silent discard behavior is desired, configure `opts.ConfigureConsumer(c => c.UnhandledFailureAckResult = TransportAckResult.Ack)` and ensure no `IDeadLetterQueue` is registered in DI.
- **BC-009 (`AwsSqsMessageTransport` Concurrent Batch Execution)**:
  - **Change**: SQS poll batches are now processed concurrently using `Parallel.ForEachAsync` up to `MaxConcurrency`, rather than sequentially.
  - **Previous State**: Messages in each SQS poll response were processed sequentially on a single thread.
  - **Current State**: Multiple messages from the same receive batch execute simultaneously across thread pool workers.
  - **Affected Consumers**: Message handlers for AWS SQS that depend on sequential execution per poll batch and are not thread-safe.
  - **Migration Guidance**: Ensure message handlers are stateless or thread-safe, or set `TransportSubscriptionOptions.MaxConcurrency = 1` if strict sequential execution is required.
- **BC-010 (`InMemoryMessageTransport` Partition Routing)**:
  - **Change**: Subscriptions to in-memory destinations now route messages through partitioned channels based on `PartitionKey` instead of a single shared queue with a global semaphore.
  - **Previous State**: Any free worker could execute any message from the global channel.
  - **Current State**: Messages with the same `PartitionKey` are strictly serialized through their assigned partition channel.
  - **Affected Consumers**: Workloads expecting concurrent execution of messages that share the same `PartitionKey`.
  - **Migration Guidance**: For maximum concurrency across all messages, omit `PartitionKey` so messages are distributed round-robin across all channels, or increase `MaxConcurrency`.

## [1.0.0] - 2026-08-28

### Added
- **Batch Publishing & Transport Optimization**:
  - `IBatchMessageTransport`: Opt-in interface for bulk publish operations with native transport-level batching.
  - `IMessagePublisher.PublishBatchAsync<T>` & `SendBatchAsync<T>`: High-throughput batch publishing APIs with transparent single-message fallback.
  - Implemented `IBatchMessageTransport` in `AzureServiceBusMessageTransport` using `ServiceBusMessageBatch` memory-safe chunking.
  - Documented decision in [ADR-021: Batch Publishing and Transport Optimization](docs/adr/adr-021-batch-publishing-and-transport-optimization.md).
- **Schema Versioning & Message Upcasting**:
  - `IMessageUpcaster`: Interface for versioned contract migrations (`Upcast`).
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
  - `EricksonLopez.Messaging.RabbitMQ`: AMQP-based messaging with connection recovery, persistent delivery, and dead-lettering.
  - `InMemoryMessageTransport`: High-throughput `System.Threading.Channels` in-memory transport with backpressure support (`ChannelCapacity`, `FullMode`). Implements `IMessageTransport`, `IDeferableMessageTransport` (using `Task.Delay` and `TimeProvider`), and `IBatchMessageTransport`.
- **Testing Utilities**:
  - `EricksonLopez.Messaging.Testing`: Standalone NuGet package featuring `InMemoryTestHarness`, `PublishedMessageList`, `ConsumedMessageList`, and asynchronous wait helpers (`WaitUntilPublishedAsync`, `WaitUntilConsumedAsync`).
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
  - Full set of Architectural Decision Records (`ADR-001` through `ADR-025`).
- **`MessagePublishOptions` and `MessageSendOptions` as Mutable Sealed Classes**:
  - Established that publish/send option objects are intentionally mutable `sealed class` types using object initializer syntax (not immutable records), documented in [ADR-024](docs/adr/adr-024-publish-options-mutable-class-vs-record.md).

### Changed
- Decoupled `EricksonLopez.Messaging.Abstractions` from local project references in favor of the published `EricksonLopez.Result` NuGet package.
- Consolidated solution structure in `EricksonLopez.Messaging.slnx`.

[Unreleased]: https://github.com/ericksonlopezf/dotnet-messaging/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/ericksonlopezf/dotnet-messaging/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/ericksonlopezf/dotnet-messaging/releases/tag/v1.0.0

