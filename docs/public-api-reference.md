# Public API Reference & Type Design

This document details the public API surface, core type design, immutability contracts, thread-safety invariants, and Native AOT compatibility across all packages in the `EricksonLopez.Messaging` ecosystem.

---

## 1. Package API Surface Breakdown

### 1.1 `EricksonLopez.Messaging.Abstractions`

| Type | Kind | Description |
| :--- | :--- | :--- |
| `IMessage` | Interface | Immutable marker interface for all message contracts. |
| `IMessageHandler<in TMessage>` | Interface | Consumer contract defining `ValueTask<Result> HandleAsync(TMessage message, MessageContext context, CancellationToken cancellationToken)`. |
| `IMessagePublisher` | Interface | Publisher operations: `PublishAsync`, `PublishBatchAsync`, `SendAsync`, `SendBatchAsync`. |
| `IMessageConsumer` | Interface | Message consumer lifecycle manager contract: `StartAsync`, `StopReceivingAsync`, `DrainInFlightMessagesAsync`. |
| `IMessageSerializer` | Interface | Serialization abstraction (`Serialize<T>`, `Deserialize<T>`, `Deserialize`). |
| `IMessageUpcaster<in TOld, out TNew>` | Interface | Typed schema transformation interface: `TNew Upcast(TOld oldMessage, TransportMessageMetadata metadata)`. |
| `IDeadLetterQueue` | Interface | Dead-letter queue routing abstraction: `ForwardToDeadLetterAsync`, `ForwardRawToDeadLetterAsync`. |
| `DeadLetterReason` | Record | Captures the reason and context for forwarding a message to the dead-letter queue (`Code`, `Description`, `MessageType`, `Timestamp`). |
| `MessageTypeAttribute` | Attribute | Discriminator attribute decorating `IMessage` implementations with unique schema names. |
| `PartitionKeyAttribute` | Attribute | Decorates message properties used for transport partition routing (Kafka/ASB). |
| `TransportMessageMetadata` | Record | Carrier metadata for network transport (`MessageId`, `MessageType`, `Timestamp`, `CorrelationId`, `CausationId`, `TraceParent`, `TenantId`, `PartitionKey`, `ContentType`, `SchemaVersion`, `Headers`). |
| `MessageContext` | Sealed Class | Ambient execution context supplied to handlers (`Metadata`, `ServiceProvider`, `CancellationToken`, `Message`, `Items`). |
| `MessageEnvelope<T>` | Sealed Record | Strongly-typed message container pairing `Payload` with its `MessageContext`. |
| `MessagePublishOptions` | Sealed Class | Optional configuration for pub/sub publish operations (`Destination`, `CorrelationId`, `CausationId`, `TenantId`, `PartitionKey`, `Headers`). |
| `MessageSendOptions` | Sealed Class | Optional configuration for point-to-point send operations (`CorrelationId`, `CausationId`, `TenantId`, `PartitionKey`, `Headers`). |

---

### 1.2 `EricksonLopez.Messaging` (Core)

| Type | Kind | Description |
| :--- | :--- | :--- |
| `IMessageTransport` | Interface | Base transport contract: `PublishRawAsync`, `SubscribeAsync`. |
| `IDeferableMessageTransport` | Interface | Opt-in transport interface for delayed/scheduled message redelivery (`DeferRawAsync`). |
| `IBatchMessageTransport` | Interface | Opt-in transport interface for high-throughput batch publishing (`PublishBatchRawAsync`). |
| `TransportAckResult` | Enum | Consumer acknowledgement outcomes: `Ack`, `NackRequeue`, `DeadLetter`. |
| `TransportSubscriptionOptions` | Sealed Class | Subscription configuration (`MaxConcurrency`, `ConsumerGroup`, `PrefetchCount`). |
| `MessageExecutionDelegate` | Delegate | Represents the next middleware or handler invocation in the pipeline: `ValueTask<Result>(MessageContext, CancellationToken)`. |
| `IMessageMiddleware` | Interface | Middleware contract for the consumer pipeline: `ValueTask<Result> InvokeAsync(MessageContext context, MessageExecutionDelegate next, CancellationToken cancellationToken)`. |
| `MiddlewarePipeline` | Sealed Class | Composes and executes ordered middleware chains. |
| `ExceptionHandlingMiddleware` | Sealed Class | Catches unhandled exceptions and encapsulates them into `Result.Failure`. Converts `OperationCanceledException` to `Error.Failure("Messaging.Cancelled")`. |
| `LoggingMiddleware` | Sealed Class | High-performance structured logging with message context. Re-throws exceptions after logging; compose with `ExceptionHandlingMiddleware`. |
| `TracingMiddleware` | Sealed Class | W3C distributed tracing context propagation via `ActivitySource`. |
| `RetryMiddleware` | Sealed Class | Configurable exponential backoff retry with full-jitter randomization (`Random.Shared`) and `TimeProvider` support. Configured via `RetryOptions`. |
| `RetryOptions` | Sealed Class | Configuration options for `RetryMiddleware` (`MaxRetries`, `InitialDelay`, `TimeProvider`). |
| `CircuitBreakerMiddleware` | Sealed Class | Fast-fail protection state machine (`Closed`, `Open`, `HalfOpen`) with `TimeProvider`. |
| `CircuitBreakerOptions` | Sealed Class | Configuration options for circuit breaker thresholds and break durations (`FailureThreshold`, `SamplingDuration`, `BreakDuration`, `TimeProvider`). |
| `HandlerTimeoutMiddleware` | Sealed Class | Enforces execution deadlines using linked cancellation tokens. |
| `HandlerTimeoutOptions` | Sealed Class | Configuration options for handler timeout limits (`Timeout`, `TimeProvider`). |
| `MessageUpcastingMiddleware` | Sealed Class | Applies registered `IMessageUpcaster<TOld, TNew>` transformations before handler dispatch. |
| `NativeAotJsonSerializer` | Sealed Class | System.Text.Json serializer integrated with `JsonSerializerContext`. |
| `DefaultMessageDispatcher` | Sealed Class | Scoped handler resolution and typed dispatch engine. |
| `MessagePublisher` | Sealed Class | Core implementation of `IMessagePublisher`. |
| `MessageConsumer` | Sealed Class | Core implementation of `IMessageConsumer`. Default concurrency: `ProcessorCount * 2`; default prefetch: `20`. `AddDestination(string)` registers additional subscription destinations before `StartAsync`. |
| `InMemoryMessageTransport` | Sealed Class | Channel-based in-memory transport with configurable backpressure and deferral. |
| `InMemoryTransportOptions` | Sealed Class | Configuration for in-memory channel capacity and full modes. |
| `MessagingConsumerHostedService` | Sealed Class | `IHostedService` managing transport subscription lifecycle. |
| `MessagingHealthCheck` | Sealed Class | `IHealthCheck` reporting messaging transport health. |
| `MessagingDiagnostics` | Static Class | OpenTelemetry instrumentation source: `ActivitySourceName`, `MeterName`, `Version` constants; `ActivitySource` and `Meter` instances; `Counter<long>` instruments `MessagesPublished`, `MessagesReceived`, `MessagesFailed`; `Histogram<double>` instrument `ProcessingDuration`. |
| `MessagingServiceCollectionExtensions` | Static Class | Fluent DI extensions: `AddMessaging`, `AddMessageHandler<TMsg, THandler>`, `AddMessageUpcaster<TOld, TNew, TUpcaster>`. |
| `MessagingHealthCheckExtensions` | Static Class | Health check registration extension: `AddMessagingHealthCheck`. |
| `MessagingOptionsBuilder` | Sealed Class | Fluent pipeline builder returned by `AddMessaging(options => ...)`. Methods: `AddMiddleware<T>`, `AddExceptionHandling`, `AddLogging`, `AddTracing`, `AddRetry`, `AddCircuitBreaker`, `AddHandlerTimeout`, `AddUpcasting`. |

---

### 1.3 Transport Adapter Packages

#### `EricksonLopez.Messaging.RabbitMQ`
- **`RabbitMqMessageTransport`**: AMQP 0-9-1 driver using `RabbitMQ.Client`. Supports connection recovery and durable exchanges/queues.
- **`RabbitMqTransportOptions`**: `HostName` (default: `"localhost"`), `Port` (default: `5672`), `VirtualHost` (default: `"/"`), `UserName` (default: `"guest"`), `Password` (default: `"guest"`), `ExchangeName` (default: `""` — AMQP default exchange).
- **`RabbitMqMessagingServiceCollectionExtensions`**: `AddRabbitMqMessagingTransport(Action<RabbitMqTransportOptions>)`.

#### `EricksonLopez.Messaging.AzureServiceBus`
- **`AzureServiceBusMessageTransport`**: Driver using `Azure.Messaging.ServiceBus` and `Azure.Identity`. Implements `IMessageTransport`, `IDeferableMessageTransport`, and `IBatchMessageTransport`.
- **`AzureServiceBusTransportOptions`**: `ConnectionString` (SAS authentication), `FullyQualifiedNamespace` (Managed Identity auth, e.g. `mynamespace.servicebus.windows.net`), `Credential` (`Azure.Core.TokenCredential` for token-based auth).
- **`AzureServiceBusMessagingServiceCollectionExtensions`**: `AddAzureServiceBusMessagingTransport(Action<AzureServiceBusTransportOptions>)`.

#### `EricksonLopez.Messaging.AwsSqs`
- **`AwsSqsMessageTransport`**: AWS SQS driver. Implements `IMessageTransport` and `IDeferableMessageTransport`. Supports Standard and FIFO queues via long polling.
- **`AwsSqsTransportOptions`**: `Region` (default: `"us-east-1"`), `ServiceUrl` (optional; use for LocalStack or custom endpoints), `WaitTimeSeconds` (default: `20`; long-polling duration), `MaxNumberOfMessages` (default: `10`; messages per poll).
- **`AwsSqsMessagingServiceCollectionExtensions`**: `AddAwsSqsMessagingTransport(Action<AwsSqsTransportOptions>)`.

#### `EricksonLopez.Messaging.Kafka`
- **`KafkaMessageTransport`**: Apache Kafka driver using `Confluent.Kafka`. Implements `IMessageTransport` with partition key routing via `[PartitionKey]` and consumer group subscriptions.
- **`KafkaTransportOptions`**: `BootstrapServers` (default: `"localhost:9092"`), `GroupId` (default: `"ericksonlopez-messaging-group"`), `ClientId` (optional; broker logging/metrics identifier), `EnableAutoCommit` (default: `false`; when false, offsets committed after handler `Ack`).
- **`KafkaMessagingServiceCollectionExtensions`**: `AddKafkaMessagingTransport(Action<KafkaTransportOptions>)`.

---

### 1.4 Roslyn Tooling & Integrations

#### `EricksonLopez.Messaging.Generators`
- **`MessagingIncrementalGenerator`**: Roslyn `IIncrementalGenerator` emitting `GeneratedMessagingServiceCollectionExtensions.g.cs` (`AddGeneratedMessagingHandlers`) and `GeneratedMessagingJsonSerializerContext.g.cs` (`JsonSerializerContext`).

#### `EricksonLopez.Messaging.Analyzers`
- **`DiagnosticDescriptors`**: Static definitions for `ELMSG002`, `ELMSG004`, `ELMSG005`, `ELMSG008`, and `ELMSG010`.

#### `EricksonLopez.Messaging.Events`
- **`MessagingEventPublisher`**: Implements `IEventPublisher` from `EricksonLopez.Events.Contracts` to bridge in-process events to distributed brokers via `IMessagePublisher`.
- **`MessagingEventsOptions`**: `ThrowOnFailure` (bool, default: `true` — rethrows publish failures as exceptions), `DestinationResolver` (`Func<Type, string>?` — maps event type to broker destination; when `null`, uses `[MessageType]` attribute value).
- **`MessagingEventsServiceCollectionExtensions`**: `AddMessagingEventPublisher(Action<MessagingEventsOptions>?)`.

#### `EricksonLopez.Messaging.OpenTelemetry`
- **`MessagingTracerProviderBuilderExtensions`**: `AddMessagingInstrumentation()` integrating with OpenTelemetry tracing and metrics.

#### `EricksonLopez.Messaging.Testing`
- **`InMemoryTestHarness`**: Test harness implementing `IMessageTransport` for validating published and consumed messages at the raw transport level. Methods: `PublishRawAsync`, `SubscribeAsync`, `WaitUntilPublishedAsync(messageType, timeout)`, `WaitUntilConsumedAsync(messageType, timeout)`.
- **`PublishedMessageList`**: Thread-safe queryable list of published message envelopes (`Destination`, `Payload`, `Metadata`).
- **`ConsumedMessageList`**: Thread-safe queryable list of consumed message envelopes (`Destination`, `Payload`, `Metadata`, `Succeeded`).

---

## 2. Core Type Design Principles

1. **Immutability by Default**:
   - Message definitions are designed as C# `sealed record` types with `init`-only properties or positional record parameters.
   - `TransportMessageMetadata` is an immutable `record` — all fields are positional constructor parameters.
   - `MessagePublishOptions` and `MessageSendOptions` are **mutable `sealed class` types** with `{ get; set; }` properties. They are provided as optional configuration objects and are not shared across message lifecycles.
2. **Sealed Class Invariant**:
   - Middleware, dispatchers, and serializer classes are declared as `sealed` to prevent unintended inheritance and enable runtime devirtualization optimizations.
3. **Allocation Minimization**:
   - Handlers and pipeline middlewares return `ValueTask<Result>` rather than heap-allocated `Task<Result>`.
   - Payloads are passed across pipeline boundaries as `ReadOnlyMemory<byte>`.

---

## 3. Native AOT & Trimming Compatibility Matrix

| Package | Target | `IsAotCompatible` | `EnableTrimAnalyzer` | Status |
| :--- | :--- | :---: | :---: | :---: |
| `EricksonLopez.Messaging.Abstractions` | `net10.0` | :white_check_mark: | :white_check_mark: | Fully AOT Compatible |
| `EricksonLopez.Messaging` | `net10.0` | :white_check_mark: | :white_check_mark: | Fully AOT Compatible |
| `EricksonLopez.Messaging.RabbitMQ` | `net10.0` | :white_check_mark: | :white_check_mark: | Fully AOT Compatible |
| `EricksonLopez.Messaging.AzureServiceBus` | `net10.0` | :white_check_mark: | :white_check_mark: | Fully AOT Compatible |
| `EricksonLopez.Messaging.AwsSqs` | `net10.0` | :white_check_mark: | :white_check_mark: | Fully AOT Compatible |
| `EricksonLopez.Messaging.Kafka` | `net10.0` | :white_check_mark: | :white_check_mark: | ⚠️ AOT Caveat — See Note |
| `EricksonLopez.Messaging.Events` | `net10.0` | :white_check_mark: | :white_check_mark: | Fully AOT Compatible |
| `EricksonLopez.Messaging.OpenTelemetry` | `net10.0` | :white_check_mark: | :white_check_mark: | Fully AOT Compatible |
| `EricksonLopez.Messaging.Testing` | `net10.0` | :white_check_mark: | :white_check_mark: | Fully AOT Compatible |
| `EricksonLopez.Messaging.Generators` | `netstandard2.0` | N/A (Compiler Tool) | N/A | Roslyn Analyzer Component |
| `EricksonLopez.Messaging.Analyzers` | `netstandard2.0` | N/A (Compiler Tool) | N/A | Roslyn Analyzer Component |

> **⚠️ Kafka AOT Caveat**: `EricksonLopez.Messaging.Kafka.Tests` suppresses trimming diagnostics `IL2072`, `IL2026`, and `IL2075`. These originate at the `Confluent.Kafka` SDK boundary and indicate potential AOT incompatibility in the underlying driver. Until `Confluent.Kafka` provides a fully AOT-verified SDK, applications using this transport with Native AOT should validate their specific usage in a smoke test. See [Technical Debt — TD-007](technical-debt.md#td-007--kafka-transport-suppresses-aot-trimming-warnings) for the tracking item.

### AOT Smoke Test

The repository includes a dedicated Native AOT validation project:

- **`tests/EricksonLopez.Messaging.AotSmokeTest`**: A `<PublishAot>true</PublishAot>` executable that references `EricksonLopez.Messaging.Abstractions`, `EricksonLopez.Messaging`, and `EricksonLopez.Messaging.Testing`. It validates that the core messaging pipeline compiles successfully under full Native AOT with `TreatWarningsAsErrors=true` and `EnableTrimAnalyzer=true`.

```bash
# Publish with Native AOT to validate AOT compatibility
dotnet publish tests/EricksonLopez.Messaging.AotSmokeTest/EricksonLopez.Messaging.AotSmokeTest.csproj -c Release
```

