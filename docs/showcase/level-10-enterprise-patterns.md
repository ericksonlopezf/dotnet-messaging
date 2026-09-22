# Level 10: Enterprise Production Patterns

## Overview
Level 10 demonstrates production-grade observability, health monitoring, transparent message schema evolution with upcasting, partition resolution, and brokerless integration testing with `InMemoryTestHarness`.

---

## 1. OpenTelemetry Distributed Tracing & Metrics

Enable end-to-end W3C distributed trace propagation and semantic metrics across publishers, transports, and consumers:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddMessagingInstrumentation();
        tracing.AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMessagingInstrumentation();
        metrics.AddOtlpExporter();
    });
```

---

## 2. ASP.NET Core Health Checks (`MessagingHealthCheck`)

Expose readiness probes to Kubernetes or load balancers:

```csharp
builder.Services.AddMessagingHealthCheck();
```

---

## 3. Schema Evolution with Upcasting (`IMessageUpcaster`)

When evolving message contracts across microservice boundaries, `IMessageUpcaster<TOld, TNew>` transforms legacy schemas transparently before handler dispatch:

```csharp
public sealed class OrderPlacedUpcaster : IMessageUpcaster<OrderPlacedEventV1, OrderPlacedEventV2>
{
    public OrderPlacedEventV2 Upcast(
        OrderPlacedEventV1 oldMessage,
        TransportMessageMetadata metadata)
    {
        return new OrderPlacedEventV2(
            OrderId: oldMessage.OrderId,
            OrderNumber: oldMessage.OrderNumber,
            TotalAmount: oldMessage.TotalAmount,
            Currency: "USD",
            Channel: "Web");
    }
}
```

Registration into the DI pipeline:
```csharp
builder.Services.AddMessaging(options =>
{
    options.AddUpcasting();
});
builder.Services.AddMessageUpcaster<OrderPlacedEventV1, OrderPlacedEventV2, OrderPlacedUpcaster>();
```

---

## 4. Integration Testing with `InMemoryTestHarness`

Execute deterministic, high-speed integration tests without running Docker containers or external message brokers:

```csharp
[Fact]
public async Task Should_Publish_And_Consume_Message()
{
    await using var harness = new InMemoryTestHarness();

    var payload = new byte[] { 0x7B, 0x7D };
    var metadata = TransportMessageMetadata.Create("orders.placed.v1");

    await harness.PublishRawAsync("orders.placed.v1", payload, metadata);

    // Assert published message collection
    Assert.True(harness.PublishedMessages.Contains("orders.placed.v1"));
    Assert.Single(harness.PublishedMessages.OfType("orders.placed.v1"));

    // Subscribe and verify consumption
    await harness.SubscribeAsync(
        "orders.placed.v1",
        (bytes, meta, ct) => ValueTask.FromResult(TransportAckResult.Ack));

    Assert.True(harness.ConsumedMessages.AnySucceeded("orders.placed.v1"));
}
```
