# ADR-024: MessagePublishOptions and MessageSendOptions as Mutable Sealed Classes

## Status
Accepted — August 2026

## Context
Early documentation drafts and initial design exploration described `MessagePublishOptions` and `MessageSendOptions` as immutable `sealed record` types with positional constructor parameters (e.g., `new MessagePublishOptions(Topic: "orders", PartitionKey: customerId)`).

However, the actual implementation chose `sealed class` with mutable `{ get; set; }` properties. This decision was never formally documented, causing a systematic divergence between documentation and code that resulted in non-compiling examples across `quickstart.md`, `cookbook.md`, and `public-api-reference.md`.

## Decision

`MessagePublishOptions` and `MessageSendOptions` are implemented as **mutable `sealed class` types** for the following reasons:

1. **Fluent object initializer ergonomics**: The object initializer syntax (`new MessagePublishOptions { Destination = "...", PartitionKey = "..." }`) is conventional in the .NET ecosystem for optional configuration objects. It allows partial specification without requiring all optional parameters at once.

2. **Extensibility without breaking changes**: Adding new optional properties to a mutable class is non-breaking. Adding positional parameters to a record is a source-breaking change that affects all callers.

3. **Options pattern consistency**: Options objects in the .NET ecosystem (e.g., `HttpClientOptions`, `JsonSerializerOptions`) are conventionally mutable classes, not records. Using the same pattern improves API familiarity.

4. **Configuration via DI**: Options instances may be configured by multiple middleware or DI-registered services before being passed to the transport.

```csharp
// Correct usage: object initializer with mutable class
var options = new MessagePublishOptions
{
    Destination = "orders.topic",
    PartitionKey = customerId,
    CorrelationId = correlationId
};

await publisher.PublishAsync(message, options);
```

## Consequences
- `MessagePublishOptions` and `MessageSendOptions` are **not immutable** and must not be documented as immutable records.
- All documentation must use object initializer syntax (`new X { Prop = value }`) rather than positional record constructor syntax (`new X(Prop: value)`).
- The `Destination` property (not `Topic`, `Queue`, or `Exchange`) is the canonical routing destination for both options types.
- A `MessagePublishOptions` instance should not be shared across concurrent `PublishAsync` calls without external synchronization, as the properties are mutable.
