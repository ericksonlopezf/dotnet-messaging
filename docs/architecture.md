# EricksonLopez.Messaging — Architecture Blueprint & Technical Diagrams

## 1. System Overview & Core Tenets

`EricksonLopez.Messaging` is an enterprise-grade, high-throughput, distributed messaging and publish/subscribe framework designed for .NET 10 microservices and event-driven architectures. It prioritizes **Native AOT compilation**, **low-allocation hot paths**, and **functional error handling** via `Result<T>` (`EricksonLopez.Result`).

The guiding architectural tenets of the ecosystem are:
1. **Clean Layer Isolation**: Domain models and application services remain 100% agnostic of physical broker topologies, exchange types, and partition routing mechanics.
2. **Zero Runtime Reflection (Native AOT First)**: Handler discovery, invocation dispatching, and JSON serialization are resolved at compile time using Roslyn Incremental Generators (`MessagingIncrementalGenerator`) and `JsonSerializerContext`.
3. **Functional Flow Control (`Result`)**: Elimination of exceptions for expected business rejections or validation failures, preventing expensive CLR stack unwinds on hot consumer loops.
4. **Two-Way Interceptor Pipeline**: Composable Russian-doll middleware pipeline for cross-cutting concerns: resilience, auditing, distributed tracing, and contract schema evolution.
5. **High Performance & Minimal Allocations**: Pervasive utilization of `ValueTask<Result>`, `ReadOnlyMemory<byte>`, and zero-copy `IBufferWriter<byte>` pipelines.
6. **Native Observability**: Built-in adherence to OpenTelemetry Semantic Conventions v1.26+ via dedicated `ActivitySource` and `Meter` instruments.

---

## 2. Official Architectural Blueprints

### 2.1 Diagram 1: System Overview Architecture

```mermaid
flowchart TB
    subgraph ClientApp["Application / Domain Layer"]
        DomainLogic[Business Logic / Domain Aggregates]
        PubClient["IMessagePublisher (Publish / Send / Batch)"]
        DomainLogic --> PubClient
    end

    subgraph CoreEngine["EricksonLopez.Messaging (Core Engine)"]
        Serializer["IMessageSerializer (NativeAotJsonSerializer)"]
        Dispatcher["IMessageDispatcher (DefaultMessageDispatcher)"]
        ConsumerEngine["IMessageConsumer (MessageConsumer)"]
        HostedSvc["MessagingConsumerHostedService (BackgroundService)"]
        PipelineEngine["MiddlewarePipeline (Interceptor Chain)"]
        DedupEngine["IMessageDeduplicationStore"]
    end

    subgraph TransportAdapters["Pluggable Transport Adapters (IMessageTransport)"]
        Mem["InMemoryMessageTransport\n(System.Threading.Channels)"]
        RMQ["RabbitMqMessageTransport\n(AMQP 0-9-1)"]
        Kafka["KafkaMessageTransport\n(Partitioned Stream Client)"]
        ASB["AzureServiceBusMessageTransport\n(Queues, Topics & Batches)"]
        SQS["AwsSqsMessageTransport\n(Standard / FIFO & Polling)"]
    end

    subgraph ExternalBrokers["Physical Brokers / Cloud Services"]
        CloudRMQ[(RabbitMQ Cluster)]
        CloudKafka[(Apache Kafka Cluster)]
        CloudASB[(Azure Service Bus)]
        CloudSQS[(AWS SQS / LocalStack)]
    end

    PubClient --> Serializer
    PubClient --> TransportAdapters
    HostedSvc --> ConsumerEngine
    ConsumerEngine --> TransportAdapters
    ConsumerEngine --> DedupEngine
    ConsumerEngine --> PipelineEngine
    PipelineEngine --> Dispatcher

    RMQ <--> CloudRMQ
    Kafka <--> CloudKafka
    ASB <--> CloudASB
    SQS <--> CloudSQS
```

---

### 2.2 Diagram 2: Cardinal Messaging Execution Flow

```mermaid
flowchart LR
    App[Publisher Application] -->|1. PublishAsync| Pub[MessagePublisher]
    Pub -->|2. Serialize| Ser[NativeAotJsonSerializer]
    Ser -->|3. Binary Payload| TransPub[IMessageTransport]
    TransPub -->|4. Network Frame| Broker[(Message Broker)]
    Broker -->|5. Delivery Callback| TransSub[Transport.SubscribeAsync]
    TransSub -->|6. Raw Frame Delivery| Con[MessageConsumer]
    Con -->|7. Pipeline Execution| Pipe[MiddlewarePipeline]
    Pipe -->|8. Scoped Dispatch| Disp[DefaultMessageDispatcher]
    Disp -->|9. Invocation| Handler[IMessageHandler]
    Handler -->|10. Result| Ack[TransportAckResult]
    Ack -->|11. Commit / Requeue / DLQ| Broker
```

---

### 2.3 Diagram 3: End-to-End Publish and Consume Sequence

```mermaid
sequenceDiagram
    autonumber
    actor App as Application
    participant Pub as MessagePublisher
    participant Ser as NativeAotJsonSerializer
    participant Trans as IMessageTransport
    participant Broker as Broker / Queue
    participant Con as MessageConsumer
    participant Pipe as MiddlewarePipeline
    participant Disp as DefaultMessageDispatcher
    participant Scope as IServiceScope
    participant Handler as IMessageHandler<T>

    App->>Pub: PublishAsync<T>(message, options, ct)
    Pub->>Ser: Serialize<T>(message)
    Ser-->>Pub: ReadOnlyMemory<byte>
    Note over Pub: Construct TransportMessageMetadata<br/>(Id, Type, TraceParent, PartitionKey)
    Pub->>Trans: PublishRawAsync(dest, payload, metadata, ct)
    Trans->>Broker: Transmit network frame
    Broker-->>Trans: Publish acknowledgement
    Trans-->>Pub: Result.Success()
    Pub-->>App: Result.Success()

    Note over Broker,Con: --- Asynchronous Consumption Cycle ---
    Broker->>Con: Deliver raw message frame
    Con->>Pipe: ExecuteAsync(context, terminal, ct)
    Pipe->>Disp: DispatchAsync(type, payload, metadata, ct)
    Disp->>Scope: CreateScope()
    Disp->>Handler: HandleAsync(message, context, ct)
    Handler-->>Disp: ValueTask<Result>
    Disp-->>Pipe: Result
    Pipe-->>Con: Result
    alt Result.IsSuccess
        Con->>Broker: TransportAckResult.Ack
    else Result.IsFailure (transient/retryable)
        Con->>Broker: TransportAckResult.NackRequeue
    else Result.IsFailure (fatal/poison/invalid)
        Con->>Broker: TransportAckResult.DeadLetter
    end
```

---

### 2.4 Diagram 4: Consumer & Circuit Breaker State Machines

```mermaid
stateDiagram-v2
    state "MessageConsumer Lifecycle" as ConsumerLifecycle {
        [*] --> Stopped : Instantiation
        Stopped --> Initializing : StartAsync(ct)
        Initializing --> Receiving : SubscribeAsync on transport
        Receiving --> Draining : StopReceivingAsync()
        Draining --> Stopped : DrainInFlightMessagesAsync() (inFlight == 0)
        Stopped --> Disposed : DisposeAsync()
    }

    state "Circuit Breaker State Machine" as CircuitBreakerSM {
        [*] --> Closed : Initial Normal State
        Closed --> Closed : Handler execution succeeds
        Closed --> Open : Consecutive failures >= FailureThreshold (within SamplingDuration window)
        Open --> Open : Incoming messages fast-fail immediately
        Open --> HalfOpen : BreakDuration expires (TimeProvider)
        HalfOpen --> Closed : Probe message succeeds
        HalfOpen --> Open : Probe message fails
    }
```

---

### 2.5 Diagram 5: Package and Component Dependencies

```mermaid
graph TD
    subgraph AbstractionsLayer["Layer 0: Pure Contracts"]
        Abs["EricksonLopez.Messaging.Abstractions<br/>• IMessage<br/>• IMessageHandler<br/>• IMessagePublisher<br/>• IMessageConsumer<br/>• IMessageSerializer<br/>• IMessageUpcaster<br/>• IDeadLetterQueue<br/>• IMessageDeduplicationStore<br/>• IPartitionKeyResolver<br/>• MessageContext<br/>• TransportMessageMetadata"]
    end

    subgraph CoreEngineLayer["Layer 1: Core Messaging Engine"]
        Core["EricksonLopez.Messaging<br/>• DefaultMessageDispatcher<br/>• MessagePublisher & Consumer<br/>• MiddlewarePipeline & Resiliency<br/>• InMemoryMessageTransport<br/>• MessagingConsumerHostedService<br/>• NativeAotJsonSerializer"]
    end

    subgraph TransportAdaptersLayer["Layer 2: Transport Infrastructure Drivers"]
        RabbitMQ["EricksonLopez.Messaging.RabbitMQ"]
        Kafka["EricksonLopez.Messaging.Kafka"]
        ASB["EricksonLopez.Messaging.AzureServiceBus"]
        AwsSqs["EricksonLopez.Messaging.AwsSqs"]
    end

    subgraph ExtensionsLayer["Layer 3: Integration & Observability"]
        Events["EricksonLopez.Messaging.Events"]
        OTel["EricksonLopez.Messaging.OpenTelemetry"]
        Testing["EricksonLopez.Messaging.Testing"]
    end

    subgraph RoslynLayer["Compile-Time Roslyn Tooling"]
        Generators["EricksonLopez.Messaging.Generators"]
        Analyzers["EricksonLopez.Messaging.Analyzers"]
    end

    Core --> Abs
    RabbitMQ --> Core
    Kafka --> Core
    ASB --> Core
    AwsSqs --> Core
    Events --> Abs
    OTel --> Core
    Testing --> Core
    Generators -.->|Generates AOT invokers for| Core
    Analyzers -.->|Enforces design rules on| Abs
```

---

### 2.6 Diagram 6: Russian-Doll Middleware Pipeline

```mermaid
flowchart TB
    MsgIn[Incoming Raw Message Frame] --> M1[1. ExceptionHandlingMiddleware\nConverts unhandled exceptions into Result.Failure]
    M1 --> M2[2. TracingMiddleware\nExtracts W3C traceparent and starts OTel Activity]
    M2 --> M3[3. LoggingMiddleware\nStructured logging: start, finish, duration ms]
    M3 --> M4[4. MessageDeduplicationMiddleware\nQueries IMessageDeduplicationStore to prevent duplicate execution]
    M4 --> M5[5. CircuitBreakerMiddleware\nFast-fails if downstream breaker state is Open]
    M5 --> M6[6. RetryMiddleware\nRetries with exponential backoff + randomized jitter]
    M6 --> M7[7. HandlerTimeoutMiddleware\nEnforces deadline using linked CancellationToken]
    M7 --> M8[8. MessageUpcastingMiddleware\nTransforms legacy V1 schemas to current V2]
    M8 --> M9[9. Custom Middleware\ne.g., AuditMiddleware, TenantValidationMiddleware]
    M9 --> Terminal[Terminal Invoker: DefaultMessageDispatcher.DispatchAsync]
    Terminal --> Business[Invocation: IMessageHandler.HandleAsync]
```

---

### 2.7 Diagram 7: Concurrency Control, Prefetching & Backpressure

```mermaid
flowchart TD
    subgraph TransportBuffer["Broker Delivery"]
        BrokerStream[Message Stream / Queue] -->|PrefetchCount frames| PrefetchBuffer[Transport Driver Prefetch Buffer]
    end

    subgraph ConcurrencyLimiter["Concurrency & Backpressure Limiter"]
        PrefetchBuffer --> Sem["SemaphoreSlim(MaxConcurrency)"]
        Sem -->|Slot Available| WorkerThread[Worker Execution Task]
    end

    subgraph DispatchExecution["Dispatch & Batching"]
        WorkerThread --> SingleOrBatch{Batch Operation?}
        SingleOrBatch -->|No| SingleDispatch["DefaultMessageDispatcher.DispatchAsync"]
        SingleOrBatch -->|Yes| BatchDispatch["DefaultMessageDispatcher.DispatchBatchAsync<br/>(MessageDispatchItem[])"]
        SingleDispatch --> ExecuteSingle[Execute Handler within Scoped DI]
        BatchDispatch --> ExecuteBatch[Execute Batch Items in Parallel / Sequence]
    end

    subgraph SemaphoreRelease["Resource Release"]
        ExecuteSingle --> Rel[Release SemaphoreSlim Slot]
        ExecuteBatch --> Rel
        Rel --> InFlightDec["Decrement in-flight counter"]
    end
```

---

### 2.8 Diagram 8: Error Handling, Resilience & Dead-Letter Routing

```mermaid
flowchart TD
    Start[Handler Returns Result] --> Check{Result.IsSuccess?}
    Check -->|Yes| Ack[TransportAckResult.Ack\nMessage deleted from broker]

    Check -->|No| EvaluateFailure{Evaluate Error Type}

    EvaluateFailure -->|Transient / Retryable| RetryEval{Retries remaining in RetryMiddleware?}
    RetryEval -->|Yes| Delay[Compute Exponential Delay + Jitter\nTimeProvider.Delay]
    Delay --> ReExecute[Re-execute Handler]
    ReExecute --> Check

    RetryEval -->|Exhausted| CircuitEval{Trip Circuit Breaker?}
    CircuitEval -->|Yes| RecordFailure[Increment Circuit Failure Counter]
    RecordFailure --> NackDecision{Redelivery Policy?}
    NackDecision -->|Requeue on Broker| NackRequeue[TransportAckResult.NackRequeue]
    NackDecision -->|Route to DLQ| DLQFlow[Dead-Letter Queue Flow]

    EvaluateFailure -->|Fatal / Validation / Poison| DLQFlow

    subgraph DLQFlow["Dead-Letter Queue Flow"]
        DLQReason[Instantiate DeadLetterReason\nReasonCode, Description, Exception, Timestamp]
        DLQReason --> HasCustomDLQ{Custom IDeadLetterQueue Registered?}
        HasCustomDLQ -->|Yes| ForwardDLQ[IDeadLetterQueue.ForwardToDeadLetterAsync]
        HasCustomDLQ -->|No| BrokerDLQ[TransportAckResult.DeadLetter\nBroker routes to native dead-letter exchange]
        ForwardDLQ --> BrokerDLQ
    end
```
