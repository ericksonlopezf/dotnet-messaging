# Public API Reference & Type Design — EricksonLopez.Messaging

This document constitutes the canonical inventory and technical reference for the public API surface of the `EricksonLopez.Messaging` ecosystem. All specifications are derived and verified directly against production assemblies on `.NET 10 (net10.0)` and `.NET Standard 2.0`.

---

## 1. Comprehensive Public API Inventory

The table below lists the public types exported across Core, Abstractions, Transports, and Diagnostics packages:

| Type Name | Namespace | Responsibility | Dependencies | Use Cases | Complexity | Reference Code |
| :--- | :--- | :--- | :--- | :--- | :---: | :---: |
| `MessageTypeAttribute` | `EricksonLopez.Messaging.Attributes` | Decorates message contracts with a canonical wire schema identifier. | `System.Attribute` | Broker-agnostic routing without CLR type coupling. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1457) |
| `PartitionKeyAttribute` | `EricksonLopez.Messaging.Attributes` | Marks a message property as the partition key for Kafka and Azure Service Bus. | `System.Attribute` | Preserving sequential ordering across topic partitions. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1519) |
| `DeadLetterReason` | `EricksonLopez.Messaging.Contracts` | Encapsulates error codes, exception types, and timestamps when dead-lettering. | `System.DateTimeOffset`, `System.Exception` | Forensic diagnostics of poisoned or unprocessable messages. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1856) |
| `IDeadLetterQueue` | `EricksonLopez.Messaging.Contracts` | Application contract for typed (`ForwardToDeadLetterAsync`) or binary dead-letter routing. | `EricksonLopez.Result`, `MessageContext` | Custom database or broker-backed dead-letter adapters. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1856) |
| `IMessage` | `EricksonLopez.Messaging.Contracts` | Pure marker interface for immutable distributed messages, commands, and events. | None | Strongly-typed message contract definitions. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1458) |
| `IMessageConsumer` | `EricksonLopez.Messaging.Contracts` | Manages message consumer lifecycle: start, stop receiving, and graceful drain. | `System.Threading.CancellationToken` | Consumer orchestration, backpressure, and host shutdown. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L173) |
| `IMessageDeduplicationStore` | `EricksonLopez.Messaging.Contracts` | Storage contract for atomic idempotency lease acquisition and release. | `System.IDisposable`, `System.TimeSpan` | Distributed message deduplication under at-least-once delivery. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1325) |
| `IMessageHandler<in TMessage>` | `EricksonLopez.Messaging.Contracts` | Defines asynchronous message processing returning `ValueTask<Result>`. | `EricksonLopez.Result`, `MessageContext` | Business logic execution for inbound messages. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1500) |
| `IMessagePublisher` | `EricksonLopez.Messaging.Contracts` | 1:N pub/sub (`PublishAsync`, `PublishBatchAsync`) and 1:1 commands (`SendAsync`, `SendBatchAsync`). | `EricksonLopez.Result`, `MessagePublishOptions` | Outbound event and command emission to the bus. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L177) |
| `IMessageSerializer` | `EricksonLopez.Messaging.Contracts` | Binary serialization contract supporting direct `IBufferWriter<byte>` writes. | `System.ReadOnlyMemory<byte>`, `IBufferWriter<byte>` | Pluggable format drivers (JSON, Protobuf, MessagePack). | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1791) |
| `IMessageUpcaster<in TOld, out TNew>` | `EricksonLopez.Messaging.Contracts` | Versioned schema migration transforming legacy payloads to current contracts. | `TransportMessageMetadata`, `IMessage` | Zero-downtime message contract evolution. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1711) |
| `IPartitionKeyResolver` | `EricksonLopez.Messaging.Contracts` | Resolves message partition keys dynamically at runtime without reflection. | None | Runtime partition calculation for partitioned brokers. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1264) |
| `MessageContext` | `EricksonLopez.Messaging.Contracts` | Ambient context carrying metadata, scoped service provider, and cancellation. | `TransportMessageMetadata`, `IServiceProvider` | Contextual DI resolution inside handlers and middlewares. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1503) |
| `MessageDispatchItem` | `EricksonLopez.Messaging.Contracts` | High-performance readonly struct bundling raw payload and metadata for batch dispatch. | `TransportMessageMetadata`, `ReadOnlyMemory<byte>` | Zero-allocation bulk message processing. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1382) |
| `MessageEnvelope<TMessage>` | `EricksonLopez.Messaging.Contracts` | Strongly-typed carrier pairing a typed payload with `TransportMessageMetadata`. | `TransportMessageMetadata` | Formal message packaging and wire logging. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L443) |
| `MessagePublishOptions` | `EricksonLopez.Messaging.Contracts` | Configurable options for publish operations (`Destination`, `CorrelationId`, `TenantId`). | `IReadOnlyDictionary<string, string>` | Customizing publication metadata and routing overrides. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L345) |
| `MessageSendOptions` | `EricksonLopez.Messaging.Contracts` | Configurable options for point-to-point command routing (`CorrelationId`, `Headers`). | `IReadOnlyDictionary<string, string>` | Point-to-point command metadata customization. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L379) |
| `TransportMessageMetadata` | `EricksonLopez.Messaging.Contracts` | Immutable record carrying message identifiers, traceparent, timestamps, and headers. | `System.DateTimeOffset`, `IReadOnlyDictionary<string, string>` | Cross-network distributed context propagation. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L198) |
| `MessagingDiagnostics` | `EricksonLopez.Messaging.Diagnostics` | Canonical constants exposing OTel `ActivitySource`, `Meter`, and five instruments: `MessagesPublished`, `MessagesReceived`, `MessagesFailed`, `MessagesDeduplicated`, `ProcessingDuration`. | `ActivitySource`, `Meter`, `Counter<long>`, `Histogram<double>` | Standardized distributed tracing and metrics configuration. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L142) |
| `DefaultMessageDispatcher` | `EricksonLopez.Messaging.Dispatch` | Core dispatcher managing DI scoping, middleware pipelines, and handler invocation. | `IMessageSerializer`, `IMessageMiddleware`, `IHandlerRegistry` | Inbound message dispatch to scoped application handlers. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1034) |
| `DefaultMessageDispatcher.HandlerBinding` | `EricksonLopez.Messaging.Dispatch` | Compiled record representing a zero-reflection handler invoker mapping. | `System.Type`, `System.Func` | Devirtualized compile-time handler invocation. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1394) |
| `HandlerRegistrationBase` | `EricksonLopez.Messaging.Dispatch` | Abstract base storing `TypeName` and registering invokers into the dispatcher. | `IHandlerRegistration` | Foundation for generated and manual handler bindings. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1513) |
| `IHandlerRegistration` | `EricksonLopez.Messaging.Dispatch` | Contract defining handler registration against dispatcher instances. | `DefaultMessageDispatcher`, `IHandlerRegistry` | Compile-time source generator handler binding support. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1377) |
| `IHandlerRegistry` | `EricksonLopez.Messaging.Dispatch` | Contract exposing `RegisterHandler<TMessage, THandler>(string typeName)`. | None | Imperative registration decoupling handlers from concrete dispatchers. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1353) |
| `IMessageDispatcher` | `EricksonLopez.Messaging.Dispatch` | Central dispatch contract (`DispatchAsync`, `DispatchBatchAsync`). | `EricksonLopez.Result`, `TransportMessageMetadata` | Decoupling transport drivers from message handler invocation. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1034) |
| `MessageConsumer` | `EricksonLopez.Messaging.Dispatch` | Core consumer connecting transport subscriptions to the dispatcher pipeline. | `IMessageTransport`, `IMessageDispatcher`, `MessageConsumerOptions` | Continuous message ingestion, concurrency limiting, and ack. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L173) |
| `MessageConsumerOptions` | `EricksonLopez.Messaging.Dispatch` | Consumer tuning options (`MaxConcurrency`, `PrefetchCount`, `UnhandledFailureAckResult`). | `TransportAckResult` | Parallelism throttling and unhandled failure management. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L241) |
| `MessagePublisher` | `EricksonLopez.Messaging.Dispatch` | Publisher implementing `IMessagePublisher`; serializes, resolves keys, delegates to transport. | `IMessageTransport`, `IMessageSerializer` | Universal message publication engine. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L177) |
| `MessagingHealthCheck` | `EricksonLopez.Messaging.HealthChecks` | ASP.NET Core `IHealthCheck` reporting `Healthy` or `Degraded` bus availability. | `IMessagePublisher`, `IHealthCheck` | Kubernetes readiness and liveness probes (`/health`). | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L299) |
| `MessagingHealthCheckExtensions` | `EricksonLopez.Messaging` | Extension method `services.AddMessagingHealthCheck()`. | `IServiceCollection` | Fluent health check registration in DI. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L274) |
| `MessagingConsumerHostedService` | `EricksonLopez.Messaging.Hosting` | `BackgroundService` starting consumers and gracefully draining in-flight messages. | `IMessageConsumer`, `BackgroundService` | Integration with .NET Generic Host lifecycle. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L173) |
| `CircuitBreakerMiddleware` | `EricksonLopez.Messaging.Middleware` | State machine middleware (`Closed`, `Open`, `HalfOpen`) with `TimeProvider` support. | `IOptions<CircuitBreakerOptions>`, `TimeProvider` | Fast-failing protection against cascading downstream outages. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L205) |
| `CircuitBreakerOptions` | `EricksonLopez.Messaging.Middleware` | Parameters for circuit breaking (`FailureThreshold`, `SamplingDuration` — sliding observation window, `BreakDuration`). | `TimeProvider` | Resiliency threshold customization. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L205) |
| `ExceptionHandlingMiddleware` | `EricksonLopez.Messaging.Middleware` | Catches unhandled exceptions and translates them into `Result.Failure(Error.Unexpected)`. | `EricksonLopez.Result` | Preventing consumer loop crashes and ensuring functional errors. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L265) |
| `HandlerTimeoutMiddleware` | `EricksonLopez.Messaging.Middleware` | Imposes maximum processing deadlines via linked `CancellationTokenSource`. | `IOptions<HandlerTimeoutOptions>`, `TimeProvider` | Cancelling stalled or hung handler executions. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L214) |
| `HandlerTimeoutOptions` | `EricksonLopez.Messaging.Middleware` | Options defining handler execution timeout (`Timeout`, `TimeProvider`). | `TimeSpan`, `TimeProvider` | Deadline configuration per consumer. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L218) |
| `IMessageMiddleware` | `EricksonLopez.Messaging.Middleware` | Interface for pipeline interceptors: `ValueTask<Result> InvokeAsync(...)`. | `MessageContext`, `MessageExecutionDelegate` | Implementing custom security, auditing, or metrics logic. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1705) |
| `IMessageUpcasterInvoker` | `EricksonLopez.Messaging.Middleware` | Abstraction for invoking typed upcasters dynamically without reflection. | `TransportMessageMetadata`, `IServiceProvider` | Dynamic schema transformation in the middleware chain. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1046) |
| `InMemoryMessageDeduplicationStore` | `EricksonLopez.Messaging.Middleware` | Thread-safe in-memory deduplication store with sliding expiration. | `ConcurrentDictionary`, `TimeProvider` | Rapid idempotency verification in single-node apps and tests. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1325) |
| `LoggingMiddleware` | `EricksonLopez.Messaging.Middleware` | Structured logging interceptor capturing contextual metadata and execution durations. | `Microsoft.Extensions.Logging.ILogger` | Auditability and observability logging in console or aggregators. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L264) |
| `MessageDeduplicationMiddleware` | `EricksonLopez.Messaging.Middleware` | Interceptor checking deduplication store leases before invoking handlers. | `IMessageDeduplicationStore`, `MessageDeduplicationOptions` | Guaranteeing effectively-once handler execution upon redelivery. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L235) |
| `MessageDeduplicationOptions` | `EricksonLopez.Messaging.Middleware` | Deduplication settings (`Expiration`, `Enabled`). | `System.TimeSpan` | Idempotency lease window configuration. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L235) |
| `MessageExecutionDelegate` | `EricksonLopez.Messaging.Middleware` | Execution delegate for the next step: `ValueTask<Result>(MessageContext, CancellationToken)`. | `EricksonLopez.Result`, `MessageContext` | Russian-doll pipeline composition. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1709) |
| `MessageUpcasterInvoker<TOld, TNew, TUpcaster>` | `EricksonLopez.Messaging.Middleware` | Strongly-typed adapter invoking `IMessageUpcaster<TOld, TNew>` via DI. | `IMessageUpcaster`, `IServiceProvider` | High-performance schema upcasting in pipelines. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1046) |
| `MessageUpcastingMiddleware` | `EricksonLopez.Messaging.Middleware` | Upgrades legacy message payloads to current contracts before dispatch. | `IEnumerable<IMessageUpcasterInvoker>`, `ILogger` | Transparent schema migration in long-lived topologies. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L232) |
| `MiddlewarePipeline` | `EricksonLopez.Messaging.Middleware` | Compiles and executes the ordered middleware chain (`BuildChain`, `ExecuteAsync`). | `IEnumerable<IMessageMiddleware>` | Efficient middleware pipeline orchestration. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1335) |
| `RetryMiddleware` | `EricksonLopez.Messaging.Middleware` | Automatic retry interceptor with exponential backoff and randomized full jitter. | `RetryOptions`, `TimeProvider` | Transparent mitigation of transient network and storage glitches. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L224) |
| `RetryOptions` | `EricksonLopez.Messaging.Middleware` | Retry settings (`MaxRetries`, `InitialDelay`, `MaxDelay`, `TimeProvider`, `ShouldRetry`). | `TimeProvider`, `Error` | Algorithmic retry customization. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L224) |
| `TracingMiddleware` | `EricksonLopez.Messaging.Middleware` | Injects W3C TraceContext headers and starts OpenTelemetry activities. | `System.Diagnostics.Activity` | Native OpenTelemetry distributed trace propagation. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L263) |
| `MessagingJsonContext` | `EricksonLopez.Messaging.Serialization` | Source-generated `JsonSerializerContext` for Native AOT metadata serialization. | `JsonSerializerContext` | Reflection-free JSON serialization for Native AOT. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1022) |
| `NativeAotJsonSerializer` | `EricksonLopez.Messaging.Serialization` | High-performance `System.Text.Json` serializer using source generators and buffer writers. | `IMessageSerializer`, `IJsonTypeInfoResolver` | Zero-allocation byte writer serialization path. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L171) |
| `IBatchMessageTransport` | `EricksonLopez.Messaging.Transport` | Opt-in transport interface for bulk raw publish operations (`PublishBatchRawAsync`). | `IMessageTransport`, `EricksonLopez.Result` | Native transport batch publishing optimization. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1729) |
| `IDeferableMessageTransport` | `EricksonLopez.Messaging.Transport` | Opt-in transport interface for scheduled message delivery (`DeferRawAsync`). | `IMessageTransport`, `System.TimeSpan` | Broker-native scheduled messages or deferred redelivery. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1318) |
| `IMessageTransport` | `EricksonLopez.Messaging.Transport` | Foundational transport abstraction for raw publish (`PublishRawAsync`) and subscribe. | `EricksonLopez.Result`, `TransportMessageMetadata` | Universal abstraction layer for any physical message broker. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1737) |
| `InMemoryMessageTransport` | `EricksonLopez.Messaging.Transport.InMemory` | In-memory transport powered by `System.Threading.Channels` with batch and defer support. | `IMessageTransport`, `IBatchMessageTransport` | Local testing, single-node architectures, and rapid development. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L171) |
| `InMemoryTransportOptions` | `EricksonLopez.Messaging.Transport.InMemory` | Channel configuration options (`ChannelCapacity`, `FullMode`). | `BoundedChannelFullMode` | In-memory backpressure and saturation management. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L304) |
| `TransportAckResult` | `EricksonLopez.Messaging.Transport` | Enum denoting transport acknowledgement outcome (`Ack`, `NackRequeue`, `DeadLetter`). | `System.Enum` | Transport-level acknowledgement signal to underlying brokers. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1286) |
| `TransportSubscriptionOptions` | `EricksonLopez.Messaging.Transport` | Network-level subscription tuning (`MaxConcurrency`, `PrefetchCount`, `ConsumerGroup`). | None | Worker thread pool and prefetch tuning. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L308) |
| `MessagingOptionsBuilder` | `Microsoft.Extensions.DependencyInjection` | Fluent builder for configuring middlewares, consumer options, and serializers. | `IServiceCollection` | Expressive bus setup in `AddMessaging(options => ...)`. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L202) |
| `MessagingServiceCollectionExtensions` | `Microsoft.Extensions.DependencyInjection` | Core DI registration methods: `AddMessaging`, `AddMessageHandler`, `AddMessageUpcaster`. | `IServiceCollection` | Service collection wiring for the messaging framework. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L171) |
| `RabbitMqMessageTransport` | `EricksonLopez.Messaging.Transport.RabbitMQ` | AMQP 0-9-1 transport driver for RabbitMQ with auto-reconnection and topology creation. | `RabbitMQ.Client`, `RabbitMqTransportOptions` | Enterprise AMQP pub/sub on RabbitMQ clusters. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1071) |
| `RabbitMqTransportOptions` | `EricksonLopez.Messaging.Transport.RabbitMQ` | AMQP connection settings (`HostName`, `Port`, `VirtualHost`, `UserName`, `Password`). | None | Connection and authentication configuration for RabbitMQ. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1071) |
| `RabbitMqMessagingServiceCollectionExtensions` | `Microsoft.Extensions.DependencyInjection` | Extension method `services.AddRabbitMqMessagingTransport(...)`. | `IServiceCollection`, `RabbitMqTransportOptions` | Registering RabbitMQ as the active transport driver. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1160) |
| `KafkaMessageTransport` | `EricksonLopez.Messaging.Transport.Kafka` | Driver for Apache Kafka with partition key routing and consumer group offset management. | `Confluent.Kafka`, `KafkaTransportOptions` | High-throughput distributed event streaming. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1082) |
| `KafkaTransportOptions` | `EricksonLopez.Messaging.Transport.Kafka` | Kafka cluster settings (`BootstrapServers`, `GroupId`, `ClientId`, `EnableAutoCommit`). | None | Client configuration for Apache Kafka. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1082) |
| `KafkaMessagingServiceCollectionExtensions` | `Microsoft.Extensions.DependencyInjection` | Extension method `services.AddKafkaMessagingTransport(...)`. | `IServiceCollection`, `KafkaTransportOptions` | Registering Apache Kafka as the active transport driver. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1163) |
| `MessagingIncrementalGenerator` | `EricksonLopez.Messaging.Generators` | Roslyn Incremental Source Generator discovering handlers and emitting `GeneratedMessagingJsonSerializerContext`. | `IIncrementalGenerator` | Compile-time Native AOT serialization and zero-reflection dispatch. | Advanced | [MessagingIncrementalGenerator.cs](../src/EricksonLopez.Messaging.Generators/MessagingIncrementalGenerator.cs) |
| `DomainBoundaryAnalyzer` | `EricksonLopez.Messaging.Analyzers` | Roslyn analyzer enforcing domain boundary separation rules (`ELMSG002`). | `DiagnosticAnalyzer` | Compile-time architectural boundary validation. | Advanced | [DomainBoundaryAnalyzer.cs](../src/EricksonLopez.Messaging.Analyzers/Analyzers/DomainBoundaryAnalyzer.cs) |
| `HandlerAsyncAnalyzer` | `EricksonLopez.Messaging.Analyzers` | Roslyn analyzer enforcing async non-blocking handler rules (`ELMSG008`). | `DiagnosticAnalyzer` | Compile-time thread-pool starvation prevention. | Advanced | [HandlerAsyncAnalyzer.cs](../src/EricksonLopez.Messaging.Analyzers/Analyzers/HandlerAsyncAnalyzer.cs) |
| `HandlerMustReturnResultAnalyzer` | `EricksonLopez.Messaging.Analyzers` | Roslyn analyzer enforcing handlers return `ValueTask<Result>` (`ELMSG010`). | `DiagnosticAnalyzer` | Compile-time functional error contract validation. | Advanced | [HandlerMustReturnResultAnalyzer.cs](../src/EricksonLopez.Messaging.Analyzers/Analyzers/HandlerMustReturnResultAnalyzer.cs) |
| `InvalidHandlerLifetimeAnalyzer` | `EricksonLopez.Messaging.Analyzers` | Roslyn analyzer enforcing exclusively **Scoped** handler lifetimes (`ELMSG004`). Flags `AddSingleton` and `AddTransient` as errors — `Transient` is prohibited alongside `Singleton`. | `DiagnosticAnalyzer` | Compile-time DI registration validation. | Advanced | [InvalidHandlerLifetimeAnalyzer.cs](../src/EricksonLopez.Messaging.Analyzers/Analyzers/InvalidHandlerLifetimeAnalyzer.cs) |
| `MessageTypeAttributeAnalyzer` | `EricksonLopez.Messaging.Analyzers` | Roslyn analyzer enforcing `[MessageType]` attribute on `IMessage` contracts (`ELMSG005`). | `DiagnosticAnalyzer` | Compile-time message contract validation. | Advanced | [MessageTypeAttributeAnalyzer.cs](../src/EricksonLopez.Messaging.Analyzers/Analyzers/MessageTypeAttributeAnalyzer.cs) |
| `DiagnosticDescriptors` | `EricksonLopez.Messaging.Analyzers` | Shared catalog of Roslyn Diagnostic Descriptors (`ELMSG002` through `ELMSG010`). | `DiagnosticDescriptor` | Compiler diagnostic definitions. | Intermediate | [DiagnosticDescriptors.cs](../src/EricksonLopez.Messaging.Analyzers/Rules/DiagnosticDescriptors.cs) |
| `AzureServiceBusMessageTransport` | `EricksonLopez.Messaging.Transport.AzureServiceBus` | Driver for Azure Service Bus with Managed Identity, native batching, and scheduling. | `Azure.Messaging.ServiceBus` | Enterprise cloud messaging on Azure Service Bus. | Advanced | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1098) |
| `AzureServiceBusTransportOptions` | `EricksonLopez.Messaging.Transport.AzureServiceBus` | Settings for Azure SB (`ConnectionString`, `FullyQualifiedNamespace`, `Credential`). | `TokenCredential` | Azure connection and Azure AD authentication setup. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1098) |
| `AzureServiceBusMessagingServiceCollectionExtensions` | `Microsoft.Extensions.DependencyInjection` | Extension method `services.AddAzureServiceBusMessagingTransport(...)`. | `IServiceCollection` | Registering Azure Service Bus as the active transport driver. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1166) |
| `AwsSqsMessageTransport` | `EricksonLopez.Messaging.Transport.AwsSqs` | Driver for AWS SQS supporting Standard and FIFO queues and long polling. | `Amazon.SQS`, `AwsSqsTransportOptions` | Cloud messaging in AWS and LocalStack test containers. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1125) |
| `AwsSqsTransportOptions` | `EricksonLopez.Messaging.Transport.AwsSqs` | Settings for AWS SQS (`Region`, `ServiceUrl`, `WaitTimeSeconds`, `MaxNumberOfMessages`). | None | Polling and region configuration for Amazon SQS. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1125) |
| `AwsSqsMessagingServiceCollectionExtensions` | `Microsoft.Extensions.DependencyInjection` | Extension method `services.AddAwsSqsMessagingTransport(...)`. | `IServiceCollection` | Registering AWS SQS as the active transport driver. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1169) |
| `MessagingEventPublisher` | `EricksonLopez.Messaging.Events` | Bridge implementing `IEventPublisher` (`EricksonLopez.Events.Contracts`) over messaging. | `IMessagePublisher`, `MessagingEventsOptions` | Routing in-process domain events out to distributed brokers. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1146) |
| `MessagingEventsOptions` | `EricksonLopez.Messaging.Events` | Event bridge options (`ThrowOnFailure`, `DestinationResolver`). | `System.Func<Type, string>` | Destination resolution and failure tolerance for event bridging. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1146) |
| `MessagingEventsServiceCollectionExtensions` | `EricksonLopez.Messaging.Events` | Extension method `services.AddMessagingEventPublisher(...)`. | `IServiceCollection` | Registering the domain events bridge into DI. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1172) |
| `MessagingMeterProviderBuilderExtensions` | `OpenTelemetry.Metrics` | Extension method `AddMessagingInstrumentation(MeterProviderBuilder)`. | `MeterProviderBuilder` | Enabling messaging metric counters in OpenTelemetry. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L297) |
| `MessagingTracerProviderBuilderExtensions` | `OpenTelemetry.Trace` | Extension method `AddMessagingInstrumentation(TracerProviderBuilder)`. | `TracerProviderBuilder` | Enabling distributed tracing in OpenTelemetry. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L294) |
| `ConsumedMessage` | `EricksonLopez.Messaging.Testing` | Record representing an ingested message in the test harness. | `TransportMessageMetadata`, `ReadOnlyMemory<byte>` | Inspecting consumed messages in integration tests. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1297) |
| `ConsumedMessageList` | `EricksonLopez.Messaging.Testing` | Thread-safe assertion list of consumed messages (`Count`, `Contains`, `OfType`). | `IReadOnlyList<ConsumedMessage>` | Assertion helper for consumed messages. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1297) |
| `InMemoryTestHarness` | `EricksonLopez.Messaging.Testing` | Testing harness implementing `IMessageTransport` with async wait helpers. | `IMessageTransport`, `PublishedMessageList` | Fast, brokerless integration tests. | Intermediate | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1273) |
| `PublishedMessage` | `EricksonLopez.Messaging.Testing` | Record representing an emitted message in the test harness. | `TransportMessageMetadata`, `ReadOnlyMemory<byte>` | Asserting published payloads and destinations. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1281) |
| `PublishedMessageList` | `EricksonLopez.Messaging.Testing` | Thread-safe assertion list of published messages (`Count`, `Contains`, `OfType`). | `IReadOnlyList<PublishedMessage>` | Assertion helper for published messages. | Basic | [Program.cs](../samples/EricksonLopez.Messaging.Sample/Program.cs#L1278) |

---

## 2. Core Method Reference (Microsoft Learn Style)

### 2.1 `IMessagePublisher.PublishAsync<TMessage>`

Publishes a message with 1:N pub/sub semantics. The destination is derived automatically from the `[MessageType]` attribute unless overridden via `MessagePublishOptions.Destination`.

```csharp
ValueTask<Result> PublishAsync<TMessage>(
    TMessage message,
    MessagePublishOptions? options = null,
    CancellationToken cancellationToken = default) where TMessage : notnull;
```

- **Parameters**:
  - `message`: Immutable message instance implementing `IMessage`. Cannot be `null`.
  - `options`: Optional options customizing `CorrelationId`, `TenantId`, `PartitionKey`, `Headers`, or overriding `Destination`. If `null`, defaults are used.
  - `cancellationToken`: Cancellation token.
- **Return Value**:
  - `ValueTask<Result>`: Functional Result. `Result.Success()` when serialized and accepted by the transport; `Result.Failure(Error)` upon transport or serialization failure.
- **Exceptions**:
  - `ArgumentNullException`: When `message` is `null`.
  - `OperationCanceledException`: When `cancellationToken` is cancelled.
- **Best Practices**:
  - Define message contracts as immutable `sealed record` types.
  - Do not reuse mutable options instances across concurrent calls.
- **Performance**:
  - `ValueTask` avoids heap allocation when completion occurs synchronously in the transport buffer.

---

### 2.2 `IMessagePublisher.SendAsync<TMessage>`

Transmits a command or message with 1:1 point-to-point semantics directly to a required destination.

```csharp
ValueTask<Result> SendAsync<TMessage>(
    TMessage message,
    string destination,
    MessageSendOptions? options = null,
    CancellationToken cancellationToken = default) where TMessage : notnull;
```

- **Parameters**:
  - `message`: Immutable message or command instance.
  - `destination`: Required target queue or topic address.
  - `options`: Optional headers, correlation identifiers, and partition keys.
  - `cancellationToken`: Cancellation token.
- **When to Use**: For commands targeting a single worker queue (e.g. `payments.process.v1`).
- **When NOT to Use**: For broadcast domain events intended for multiple independent subscribers (use `PublishAsync` instead).

---

### 2.3 `IMessagePublisher.PublishBatchAsync<TMessage>` & `SendBatchAsync<TMessage>`

Bulk publishes or sends a sequence of messages, optimizing network throughput by reducing socket transitions.

```csharp
ValueTask<Result> PublishBatchAsync<TMessage>(
    IEnumerable<TMessage> messages,
    MessagePublishOptions? options = null,
    CancellationToken cancellationToken = default) where TMessage : notnull;
```

- **Remarks**: If the transport implements `IBatchMessageTransport`, frames are packed and transmitted in a single network batch (e.g. `ServiceBusMessageBatch`). Otherwise, the publisher sequentially transmits each item.

---

### 2.4 `IMessageHandler<TMessage>.HandleAsync`

The fundamental consumer contract for receiving and handling strongly-typed messages.

```csharp
ValueTask<Result> HandleAsync(
    TMessage message,
    MessageContext context,
    CancellationToken cancellationToken = default);
```

- **Parameters**:
  - `message`: Strongly-typed, validated message payload.
  - `context`: Ambient context carrying `Metadata`, scoped `ServiceProvider`, and contextual headers.
  - `cancellationToken`: Linked token observing consumer lifecycle and handler execution deadlines (`HandlerTimeoutMiddleware`).
- **Golden Rule**: Never throw exceptions for anticipated domain failures. Return `Result.Failure(Error.Validation(...))` or `Result.Failure(Error.NotFound(...))`.

---

### 2.5 `IMessageConsumer.DrainInFlightMessagesAsync`

Performs an orderly drain of currently executing messages during application shutdown (Graceful Shutdown).

```csharp
ValueTask DrainInFlightMessagesAsync(CancellationToken cancellationToken = default);
```

- **Remarks**: Awaits until active in-flight handlers reach zero. If `cancellationToken` is cancelled, the consumer terminates to respect host termination deadlines.

---

### 2.6 `IDeferableMessageTransport.DeferRawAsync`

Transmits a scheduled message for delayed visibility in brokers supporting native deferred delivery.

```csharp
ValueTask<Result> DeferRawAsync(
    string destination,
    ReadOnlyMemory<byte> payload,
    TransportMessageMetadata metadata,
    TimeSpan delay,
    CancellationToken cancellationToken = default);
```

- **Native Broker Support**:
  - `AzureServiceBusMessageTransport`: Uses `ServiceBusSender.ScheduleMessageAsync`.
  - `InMemoryMessageTransport`: Uses asynchronous timer task delay with `TimeProvider`.
  - *(Note: `AwsSqsMessageTransport`, `RabbitMqMessageTransport`, and `KafkaMessageTransport` do not implement `IDeferableMessageTransport`).*

---

## 3. Native AOT & Trimming Compatibility Matrix

| Package | Target Framework | `IsAotCompatible` | Trimming Support | Runtime Constraints |
| :--- | :---: | :---: | :---: | :--- |
| `EricksonLopez.Messaging.Abstractions` | `net10.0` | :white_check_mark: | Full | Pure BCL contracts; zero reflection. |
| `EricksonLopez.Messaging` | `net10.0` | :white_check_mark: | Full | Reflection-free dispatch and System.Text.Json serializers. |
| `EricksonLopez.Messaging.RabbitMQ` | `net10.0` | :white_check_mark: | Full | AMQP 0-9-1 driver compatible with Native AOT. |
| `EricksonLopez.Messaging.AzureServiceBus` | `net10.0` | :white_check_mark: | Full | Azure SDK AOT-compatible pipeline. |
| `EricksonLopez.Messaging.AwsSqs` | `net10.0` | :white_check_mark: | Full | AWSSDK.SQS trim-compatible client. |
| `EricksonLopez.Messaging.Kafka` | `net10.0` | :x: | Partial | Requires librdkafka C++ native interop; `IsAotCompatible=false`. |
| `EricksonLopez.Messaging.Events` | `net10.0` | :white_check_mark: | Full | In-process domain event bridge. |
| `EricksonLopez.Messaging.OpenTelemetry` | `net10.0` | :white_check_mark: | Full | OpenTelemetry Core .NET 10 instrumentation. |
| `EricksonLopez.Messaging.Testing` | `net10.0` | :white_check_mark: | Full | In-memory testing harness. |
| `EricksonLopez.Messaging.Generators` | `netstandard2.0` | N/A | Compiler Tool | Roslyn 4.8 compile-time generator. |
| `EricksonLopez.Messaging.Analyzers` | `netstandard2.0` | N/A | Compiler Tool | Roslyn 4.8 diagnostic analyzer. |
| `EricksonLopez.Messaging.AotSmokeTest` | `net10.0` | :white_check_mark: | Executable | Validated with `<PublishAot>true</PublishAot>`. |
