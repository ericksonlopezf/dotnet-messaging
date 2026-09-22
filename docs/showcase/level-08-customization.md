# Level 08: Customization & Framework Extensibility

## Overview
Level 08 demonstrates how to extend `EricksonLopez.Messaging` by implementing custom pipeline middlewares, standalone middleware execution pipelines, custom message transports, and custom serializers.

---

## 1. Custom Pipeline Interceptors (`IMessageMiddleware`)

Custom middlewares implement Russian-doll interception around message handlers:

```csharp
public sealed class AuditMiddleware : IMessageMiddleware
{
    public async ValueTask<Result> InvokeAsync(
        MessageContext context,
        MessageExecutionDelegate next,
        CancellationToken cancellationToken)
    {
        // Pre-processing: inject audit metadata or log context
        Console.WriteLine($"[Audit] Processing message: {context.Metadata.MessageType}");

        // Pass to next middleware or handler in chain
        var result = await next(context, cancellationToken);

        // Post-processing: evaluate Result outcome
        if (result.IsFailure)
        {
            Console.WriteLine($"[Audit] Failure detected: {result.Error.Code}");
        }

        return result;
    }
}
```

Registration into the DI container:
```csharp
builder.Services.AddMessaging(options =>
{
    options.AddMiddleware<AuditMiddleware>();
});
```

---

## 2. Standalone Middleware Pipeline (`MiddlewarePipeline`)

For unit testing interceptors or running pipeline stages outside the messaging bus:

```csharp
var pipeline = new MiddlewarePipeline(new IMessageMiddleware[]
{
    new AuditMiddleware(),
    new RetryMiddleware(maxRetries: 2, initialDelay: TimeSpan.FromMilliseconds(10))
});

var result = await pipeline.ExecuteAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()));
```

---

## 3. Custom Broker Transports (`IMessageTransport`)

Organizations with proprietary queue technologies or specialized protocols can implement `IMessageTransport` and optionally `IBatchMessageTransport`:

```csharp
public sealed class CustomBatchTransport : IMessageTransport, IBatchMessageTransport, IAsyncDisposable
{
    public ValueTask<Result> PublishRawAsync(
        string destination,
        ReadOnlyMemory<byte> payload,
        TransportMessageMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        // Custom wire publication
        return ValueTask.FromResult(Result.Success());
    }

    public ValueTask<Result> PublishBatchRawAsync(
        string destination,
        IReadOnlyList<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)> batch,
        CancellationToken cancellationToken = default)
    {
        // Custom bulk wire publication
        return ValueTask.FromResult(Result.Success());
    }

    public ValueTask<Result> SubscribeAsync(
        string destination,
        Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>> handler,
        TransportSubscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Custom subscription loop
        return ValueTask.FromResult(Result.Success());
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

---

## 4. Custom Serializers (`IMessageSerializer`)

To use alternative serialization formats (Protobuf, MessagePack, Avro):

```csharp
public interface IMessageSerializer
{
    string ContentType { get; }
    ReadOnlyMemory<byte> Serialize<T>(T message);
    void Serialize<T>(T message, IBufferWriter<byte> writer);
    T? Deserialize<T>(ReadOnlyMemory<byte> payload);
    object? Deserialize(ReadOnlyMemory<byte> payload, Type messageType);
}
```
