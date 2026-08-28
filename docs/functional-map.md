# Functional Map — EricksonLopez.Messaging

This document maps the complete functional execution flow of the library from message publication to handler acknowledgement.

---

## 1. Component Interaction Overview

```mermaid
flowchart TD
    subgraph Producer
        App[Application Code] -->|"PublishAsync / SendAsync / PublishBatchAsync"| Pub[MessagePublisher]
    end

    subgraph Serialization
        Pub -->|"Serialize<T>(message)"| Ser[NativeAotJsonSerializer]
        Ser -->|"ReadOnlyMemory<byte>"| Pub
    end

    subgraph Transport Layer
        Pub -->|"PublishRawAsync(destination, payload, metadata)"| Trans[IMessageTransport]
        Trans -->|"IBatchMessageTransport.PublishBatchRawAsync"| Trans
        Trans -->|"IDeferableMessageTransport.DeferRawAsync"| Trans
        Trans -->|"Enqueue / Send to Broker"| Broker[(Message Broker\nRabbitMQ / Kafka / ASB / SQS / InMemory)]
    end

    subgraph Consumer Side
        Broker -->|"Deliver payload + metadata"| Sub[IMessageTransport.SubscribeAsync callback]
        Sub -->|"(payload, metadata, ct)"| Con[MessageConsumer]
        Con -->|"Deserialize(payload, targetType)"| Deser[NativeAotJsonSerializer]
        Deser -->|"object message"| Con
    end

    subgraph Middleware Pipeline
        Con -->|"ExecuteAsync(context)"| Pipe[MiddlewarePipeline]
        Pipe --> Up[MessageUpcastingMiddleware]
        Up --> CB[CircuitBreakerMiddleware]
        CB --> Ret[RetryMiddleware]
        Ret --> TOut[HandlerTimeoutMiddleware]
        TOut --> Log[LoggingMiddleware]
        Log --> Trace[TracingMiddleware]
        Trace --> Exc[ExceptionHandlingMiddleware]
        Exc -->|"Custom IMessageMiddleware"| Cust[AuditMiddleware / TenantMiddleware]
    end

    subgraph Dispatch
        Cust -->|"DispatchAsync(message, context)"| Disp[DefaultMessageDispatcher]
        Disp -->|"CreateScope()"| Scope[IServiceScope]
        Scope -->|"Resolve IMessageHandler<T>"| Handler["IMessageHandler<TMessage>"]
        Handler -->|"ValueTask<Result>"| Disp
    end

    subgraph Acknowledgement
        Disp -->|"Result.Success → Ack\nResult.Failure → NackRequeue or DeadLetter"| Ack[TransportAckResult]
        Ack -->|"Ack / NackRequeue / DeadLetter"| Trans
        Ack -->|"Result.Failure (unretryable)"| DLQ[IDeadLetterQueue.ForwardToDeadLetterAsync]
    end
```

---

## 2. Publish Path (Entry Point → Transport)

```mermaid
sequenceDiagram
    autonumber
    participant App as Application
    participant Pub as MessagePublisher
    participant Ser as NativeAotJsonSerializer
    participant Trans as IMessageTransport

    App->>Pub: PublishAsync<T>(message, options?, ct)
    Note over Pub: Resolve destination from options.Destination<br/>or [MessageType] attribute value
    Pub->>Ser: Serialize<T>(message)
    Ser-->>Pub: ReadOnlyMemory<byte> payload
    Note over Pub: Build TransportMessageMetadata<br/>(MessageId, MessageType, CorrelationId, TenantId, PartitionKey)
    Pub->>Trans: PublishRawAsync(destination, payload, metadata, ct)
    Trans-->>Pub: Result (Success or Failure)
    Pub-->>App: Result
```

---

## 3. Consume Path (Transport → Handler → Ack)

```mermaid
sequenceDiagram
    autonumber
    participant Broker as Message Broker
    participant Consumer as MessageConsumer
    participant Ser as NativeAotJsonSerializer
    participant Pipeline as MiddlewarePipeline
    participant Disp as DefaultMessageDispatcher
    participant Scope as IServiceScope
    participant Handler as IMessageHandler<T>

    Broker->>Consumer: Deliver raw message (payload: byte[], metadata)
    Consumer->>Ser: Deserialize(payload, targetType)
    Ser-->>Consumer: object message

    Note over Consumer: Build MessageContext<br/>(Metadata, ServiceProvider, CancellationToken, Message)

    Consumer->>Pipeline: ExecuteAsync(context, terminalHandler, ct)
    Pipeline->>Disp: DispatchAsync(message, context, ct) [terminal]
    Disp->>Scope: CreateScope()
    Scope->>Handler: HandleAsync(message, context, ct)
    Handler-->>Scope: ValueTask<Result>
    Scope-->>Disp: Result
    Disp-->>Pipeline: Result
    Pipeline-->>Consumer: Result

    alt Result.IsSuccess
        Consumer->>Broker: TransportAckResult.Ack
    else Result.IsFailure (retryable)
        Consumer->>Broker: TransportAckResult.NackRequeue
    else Result.IsFailure (poison / DLQ)
        Consumer->>Broker: TransportAckResult.DeadLetter
    end
```

---

## 4. Circuit Breaker State Machine

```mermaid
stateDiagram-v2
    [*] --> Closed : Initial state

    Closed --> Closed : Success
    Closed --> Open : FailureThreshold exceeded\nwithin SamplingDuration

    Open --> HalfOpen : BreakDuration elapsed\n(TimeProvider.GetUtcNow)
    Open --> Open : New message arrives\n→ fast-fail Result.Failure

    HalfOpen --> Closed : Probe execution succeeds
    HalfOpen --> Open : Probe execution fails
```

---

## 5. Middleware Pipeline Execution Order

```mermaid
flowchart LR
    In[Incoming Message] --> E[ExceptionHandlingMiddleware\nouter: catches all throws]
    E --> T[TracingMiddleware\nW3C traceparent propagation]
    T --> L[LoggingMiddleware\nstructured log: start/end/duration]
    L --> CB[CircuitBreakerMiddleware\nfast-fail when Open]
    CB --> R[RetryMiddleware\nexp. backoff + full-jitter]
    R --> TO[HandlerTimeoutMiddleware\nlinked CancellationToken]
    TO --> U[MessageUpcastingMiddleware\nschema V1→V2 transformation]
    U --> Custom[Custom IMessageMiddleware\n(AuditMiddleware, TenantMiddleware...)]
    Custom --> D[DefaultMessageDispatcher\nscoped handler resolution]
    D --> Out[IMessageHandler<T>.HandleAsync]
```

> **Note**: AddMessaging() registers TracingMiddleware, LoggingMiddleware, and ExceptionHandlingMiddleware automatically. Additional middleware is registered via MessagingOptionsBuilder.

---

## 6. Error Handling Flow

```mermaid
flowchart TD
    Msg[Incoming Message] --> Exec[Handler Execution]
    Exec --> S{Result?}
    S -->|Success| Ack[TransportAckResult.Ack\nBroker removes message]
    S -->|Failure — transient| Ret{RetryMiddleware\nAttempts remaining?}
    Ret -->|Yes| BackOff[Wait exponential backoff + jitter\nTimeProvider.Delay]
    BackOff --> Exec
    Ret -->|Exhausted| CB{CircuitBreaker\nOpen?}
    CB -->|Yes| NR[TransportAckResult.NackRequeue\nor DeadLetter depending on transport]
    CB -->|No| DLQ[IDeadLetterQueue.ForwardToDeadLetterAsync\nor TransportAckResult.DeadLetter]
    S -->|Failure — validation/permanent| DLQ
```

---

## 7. Layer Responsibilities

| Layer | Components | Responsibility |
|---|---|---|
| **Entry Point** | IMessagePublisher.PublishAsync, SendAsync, PublishBatchAsync, SendBatchAsync | Accept typed messages from application code |
| **Serialization** | NativeAotJsonSerializer, IMessageSerializer | Bidirectional byte serialization via JsonSerializerContext |
| **Transport** | IMessageTransport, IBatchMessageTransport, IDeferableMessageTransport | Physical delivery to/from broker |
| **Consumer** | MessageConsumer, MessagingConsumerHostedService | Subscription lifecycle and raw payload receipt |
| **Middleware Pipeline** | MiddlewarePipeline, IMessageMiddleware, MessageExecutionDelegate | Cross-cutting: resiliency, logging, tracing, upcasting |
| **Dispatch** | DefaultMessageDispatcher, IMessageDispatcher | Scoped resolution and typed handler invocation |
| **Handler** | IMessageHandler<TMessage> | Business logic; returns ValueTask<Result> |
| **Acknowledgement** | TransportAckResult (Ack / NackRequeue / DeadLetter) | Signal to broker to commit, requeue, or dead-letter |
| **Dead Letter** | IDeadLetterQueue, DeadLetterReason | Poison message routing and forensics |
