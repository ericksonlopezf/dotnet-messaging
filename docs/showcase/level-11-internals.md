# Level 11: Dispatch Mechanics & Transport Internals

## Overview
Level 11 explores the low-level architectural internals of `EricksonLopez.Messaging`, including scheduled message deferral, deduplication store contracts, pre-compiled middleware execution chains, and zero-reflection handler bindings.

---

## 1. Scheduled Deferral (`IDeferableMessageTransport`)

Transports implementing `IDeferableMessageTransport` support delayed delivery to queues:

```csharp
var transport = new InMemoryMessageTransport();
var metadata = TransportMessageMetadata.Create("orders.delayed");

ValueTask<Result> deferResult = await transport.DeferRawAsync(
    destination: "orders.delayed",
    payload: new byte[] { 1, 2, 3 },
    metadata: metadata,
    delay: TimeSpan.FromSeconds(30),
    cancellationToken: cancellationToken);
```

- In `AzureServiceBusMessageTransport`: Schedules via `ServiceBusSender.ScheduleMessageAsync`.
- In `InMemoryMessageTransport`: Asynchronously delays via `TimeProvider.CreateTimer`.

---

## 2. Idempotency & Deduplication (`IMessageDeduplicationStore`)

The framework provides an atomic lock interface `IMessageDeduplicationStore` to prevent duplicate message execution:

```csharp
IMessageDeduplicationStore dedupStore = new InMemoryMessageDeduplicationStore();

// Attempt atomic acquisition
bool acquired = await dedupStore.TryAcquireAsync("msg-001", TimeSpan.FromMinutes(5));
if (!acquired)
{
    // Duplicate message detected; safely acknowledge or skip
    return;
}

try
{
    // Execute business processing
}
finally
{
    await dedupStore.ReleaseAsync("msg-001");
}
```

---

## 3. Pre-Compiled Middleware Execution Chains (`MiddlewarePipeline.BuildChain`)

To achieve maximum performance without per-message pipeline allocations, `MiddlewarePipeline` compiles the sequence of interceptors into a unified delegate chain:

```csharp
var pipeline = new MiddlewarePipeline(middlewares);
MessageExecutionDelegate chain = pipeline.BuildChain((context, ct) => ValueTask.FromResult(Result.Success()));

// Invoking the pre-compiled chain executes all middlewares with zero intermediate pipeline allocations
var result = await chain(context, cancellationToken);
```

---

## 4. Zero-Reflection Handler Bindings (`HandlerBinding`)

During application startup, handlers are compiled into `DefaultMessageDispatcher.HandlerBinding` records:

```csharp
var sampleBinding = new DefaultMessageDispatcher.HandlerBinding(
    MessageType: typeof(PingMessage),
    HandlerType: typeof(PingHandler),
    Invoker: (serviceProvider, message, context, ct) =>
        ((PingHandler)serviceProvider.GetRequiredService<PingHandler>())
            .HandleAsync((PingMessage)message, context, ct));
```

This ensures hot-path invocation delegates avoid `MethodInfo.Invoke` and boxing overhead entirely.
