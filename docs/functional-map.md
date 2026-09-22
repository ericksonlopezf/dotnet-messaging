# Functional Map & Layer Architecture — EricksonLopez.Messaging

This document specifies the end-to-end functional map and layer transition architecture of `EricksonLopez.Messaging`. It models how all components interact across the publishing, dispatching, consuming, resilience, and lifecycle workflows.

---

## 1. End-to-End Functional Architecture Map

```mermaid
flowchart TD
    subgraph AppLayer["1. Application & Ingress Layer"]
        App[Application Code] -->|"PublishAsync / SendAsync / Batch"| Pub[IMessagePublisher / MessagePublisher]
        Events[In-Process Domain Event] -->|"IEventPublisher.PublishAsync"| EvPub[MessagingEventPublisher]
        EvPub --> Pub
    end

    subgraph SerializationLayer["2. Serialization & Partitioning Layer"]
        Pub -->|"Serialize<T>(message, writer)"| Ser[IMessageSerializer / NativeAotJsonSerializer]
        Ser -->|"ReadOnlyMemory<byte>"| Pub
        Resolver[IPartitionKeyResolver / [PartitionKey]] -->|"Resolve(message)"| PartKey[Resolved Partition Key]
        PartKey --> Pub
    end

    subgraph TransportPublish["3. Transport Outbound Layer (Publishing)"]
        Pub -->|"PublishRawAsync / PublishBatchRawAsync"| TransOut[IMessageTransport / IBatchMessageTransport]
        Pub -->|"DeferRawAsync (Scheduled Delivery)"| DeferOut[IDeferableMessageTransport]
        TransOut --> Broker[(Message Broker:\nRabbitMQ / Kafka / Azure SB / AWS SQS / InMemory)]
        DeferOut --> Broker
    end

    subgraph TransportConsumer["4. Transport Inbound & Subscription Layer"]
        Broker -->|"Delivery callback with payload + metadata"| TransIn[IMessageTransport.SubscribeAsync callback]
        TransIn -->|"Increment in-flight counter"| Con[IMessageConsumer / MessageConsumer]
        HostSvc[MessagingConsumerHostedService] -->|"StartAsync / StopAsync"| Con
    end

    subgraph PersistenceDeduplication["5. Idempotency & Deduplication Layer"]
        Con -->|"TryAcquireAsync(messageId, expiration)"| DedupStore[IMessageDeduplicationStore / InMemoryMessageDeduplicationStore]
        DedupStore -->|"Acquired = False → Duplicate"| Skip[Silent Ack / Duplicate Discard]
        DedupStore -->|"Acquired = True → Proceed"| PipeIn[Processing Pipeline]
    end

    subgraph ProcessingPipeline["6. Interceptor Pipeline (Middleware Chain)"]
        PipeIn --> Exc[ExceptionHandlingMiddleware\nConverts unhandled exceptions into Result.Failure]
        Exc --> Trace[TracingMiddleware\nInjects Activity and W3C traceparent]
        Trace --> Log[LoggingMiddleware\nLogs start, finish, and elapsed ms]
        Log --> DedupMid[MessageDeduplicationMiddleware]
        DedupMid --> CB[CircuitBreakerMiddleware\nFast-fails if breaker state is Open]
        CB --> Ret[RetryMiddleware\nExponential backoff + full jitter]
        Ret --> TOut[HandlerTimeoutMiddleware\nExecution deadline via CancellationToken]
        TOut --> Up[MessageUpcastingMiddleware\nTransforms legacy schemas V1 → V2]
        Up --> CustMid[Custom Middlewares\ne.g., AuditMiddleware]
    end

    subgraph DispatchLayer["7. Strongly-Typed Dispatch Layer"]
        CustMid -->|"DispatchAsync(messageType, payload, metadata)"| Disp[IMessageDispatcher / DefaultMessageDispatcher]
        Disp -->|"CreateScope()"| Scope[IServiceScope (Scoped DI)]
        Scope -->|"Resolve IMessageHandler<T>"| Handler["IMessageHandler<TMessage>"]
        Handler -->|"HandleAsync(msg, context, ct)"| BizResult[ValueTask<Result>]
    end

    subgraph ConfirmationLayer["8. Acknowledgement & Confirmation Layer"]
        BizResult -->|"Result.Success"| AckSuccess[TransportAckResult.Ack]
        BizResult -->|"Result.Failure (transient)"| AckRetry[TransportAckResult.NackRequeue]
        BizResult -->|"Result.Failure (fatal/poison)"| AckDeadLetter[TransportAckResult.DeadLetter]
        AckDeadLetter --> DLQ[IDeadLetterQueue.ForwardToDeadLetterAsync]
        AckSuccess --> BrokerCommit[Broker Confirmation / Commit Offset]
        AckRetry --> BrokerCommit
        AckDeadLetter --> BrokerCommit
    end

    subgraph CleanupLayer["9. Graceful Shutdown & Cleanup Layer"]
        Con -->|"DrainInFlightMessagesAsync"| Drain[Wait until inFlight == 0]
        Drain --> Dispose[DisposeAsync on transports, channels, and stores]
    end
```

---

## 2. Detailed Layer Transitions

### Transition 1: Application Ingress → Serialization & Metadata
1. The application invokes `IMessagePublisher.PublishAsync(message, options)` or `SendAsync(message, destination, options)`.
2. If the message originates from `EricksonLopez.Events.Contracts.IEventPublisher`, `MessagingEventPublisher` intercepts the domain event and resolves its destination via `MessagingEventsOptions.DestinationResolver` (falling back to `[MessageType]`).
3. The publisher determines the partition key: it evaluates any registered `IPartitionKeyResolver` implementations in DI; if none match, it inspects properties decorated with `[PartitionKey]`.
4. The publisher invokes `NativeAotJsonSerializer` (or a custom `IMessageSerializer`) to serialize the message payload into a `ReadOnlyMemory<byte>` buffer without dynamic heap allocations.
5. An immutable `TransportMessageMetadata` record is synthesized, capturing `MessageId` (UUID v4), `MessageType`, `CorrelationId`, `CausationId`, `Timestamp` (UTC), `TenantId`, `PartitionKey`, and W3C distributed trace headers (`traceparent`).

### Transition 2: Serialization → Network Transport Layer (Publishing)
1. If the transport implements `IBatchMessageTransport` and the caller invoked `PublishBatchAsync` or `SendBatchAsync`, messages are chunked and transmitted using `PublishBatchRawAsync`.
2. If scheduled or delayed delivery is requested, the publisher checks for `IDeferableMessageTransport` support and invokes `DeferRawAsync(destination, payload, metadata, delay)`.
3. For standard publications, `IMessageTransport.PublishRawAsync(destination, payload, metadata)` is invoked.
4. The active transport driver (RabbitMQ, Kafka, Azure Service Bus, AWS SQS, or InMemory) maps framework metadata into native transport headers and transmits the frame across the network.

### Transition 3: Transport → Consumer & Flow Control (Backpressure)
1. On application host startup, `MessagingConsumerHostedService` invokes `IMessageConsumer.StartAsync(ct)`.
2. `MessageConsumer` binds to all message destinations registered via `AddMessageHandler` by calling `IMessageTransport.SubscribeAsync`.
3. The transport enforces `TransportSubscriptionOptions` (`MaxConcurrency`, `PrefetchCount`, `ConsumerGroup`).
4. Upon receiving a message frame from the broker, the consumer atomically increments the active in-flight counter (`_inFlightMessages`).
5. When `MessageConsumerOptions.MaxConcurrency` is configured, a `SemaphoreSlim` throttles maximum concurrent message executions.

### Transition 4: Consumer → State & Deduplication (Idempotency)
1. Prior to handler invocation, if `MessageDeduplicationOptions.Enabled` is `true`, the consumer queries `IMessageDeduplicationStore.TryAcquireAsync(metadata.MessageId, expiration)`.
2. If acquisition fails (duplicate lease detected), the consumer skips processing and immediately acknowledges (`TransportAckResult.Ack`) to discard the duplicate frame from the broker without re-executing side effects.
3. If acquisition succeeds, execution proceeds into the middleware pipeline.

### Transition 5: Interceptor Pipeline (Russian-Doll Execution Chain)
1. `MiddlewarePipeline.BuildChain` assembles the pipeline in a deterministic sequence:
   - `ExceptionHandlingMiddleware`: Catches unhandled exceptions and encapsulates them into `Result.Failure(Error.Unexpected)`.
   - `TracingMiddleware`: Extracts the `traceparent` header from `metadata.TraceParent` and starts an OpenTelemetry `Activity`.
   - `LoggingMiddleware`: Logs structured message execution events and measures duration with high-resolution timestamps.
   - `CircuitBreakerMiddleware`: Evaluates circuit state; if `Open`, fast-fails immediately with `Result.Failure(Error.Failure("CircuitBreaker.Open"))`.
   - `RetryMiddleware`: Re-executes the handler pipeline upon transient failures using exponential backoff with randomized Full Jitter.
   - `HandlerTimeoutMiddleware`: Enforces a maximum execution deadline (`Timeout`) using linked cancellation tokens.
   - `MessageUpcastingMiddleware`: Detects legacy message contracts (e.g., V1) and invokes the registered `IMessageUpcaster<TOld, TNew>` to upgrade the schema to the current version (e.g., V2).
   - Custom Middlewares (e.g., `AuditMiddleware`, `TenantValidationMiddleware`): Intercept execution pre- and post-handler invocation.

### Transition 6: Strongly-Typed Dispatch → Scoped DI & Handler Invocation
1. The terminal delegate of the pipeline invokes `IMessageDispatcher.DispatchAsync(messageType, payload, metadata)`.
2. `DefaultMessageDispatcher` creates an isolated dependency injection scope (`IServiceScope`) via `IServiceScopeFactory.CreateScope()`.
3. An ambient `MessageContext` is constructed containing scoped service provider access (`context.ServiceProvider`).
4. The target `IMessageHandler<TMessage>` is resolved from the DI container.
5. The handler executes: `handler.HandleAsync(message, context, cancellationToken)`, returning `ValueTask<Result>`.

### Transition 7: Result Evaluation → Acknowledgement & Dead-Letter Routing
1. If the handler returns `Result.Success()`, the consumer signals `TransportAckResult.Ack` to the broker, removing the message from the queue and releasing broker resources.
2. If the handler returns `Result.Failure()`:
   - Transient errors signal `TransportAckResult.NackRequeue`, instructing the broker to redeliver according to native broker retry policies.
   - Fatal errors (unrecoverable validation or corrupt data) or exhausted retries signal `TransportAckResult.DeadLetter`.
   - When a custom `IDeadLetterQueue` is registered, the failure is dispatched to `ForwardToDeadLetterAsync` or `ForwardRawToDeadLetterAsync` with forensic metadata (`DeadLetterReason`).

### Transition 8: Orderly Shutdown (Graceful Drain & Cleanup)
1. Upon receiving an operating system shutdown signal (SIGTERM or `host.StopAsync`), `MessagingConsumerHostedService.StopAsync` triggers.
2. It invokes `IMessageConsumer.StopReceivingAsync()` to immediately halt ingress of new messages from the broker.
3. It invokes `IMessageConsumer.DrainInFlightMessagesAsync(ct)` to allow currently executing handlers a grace period until `_inFlightMessages == 0`.
4. Once drained, network resources, connections, and background channels are safely disposed via `DisposeAsync()`.
