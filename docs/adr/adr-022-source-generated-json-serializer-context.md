# ADR-022: Source-Generated JsonSerializerContext for Zero-Reflection Native AOT

## Status
Accepted — August 2026

## Date
2026-09-04

## Context
In .NET Native AOT and trimming scenarios, `System.Text.Json` default reflection-based serialization (`DefaultJsonTypeInfoResolver`) is disabled or generates compiler/trimming warnings (`IL2026`, `IL3050`). If message types are not pre-registered on a `JsonSerializerContext`, serialization fails at runtime with `NotSupportedException` in Native AOT binaries.

Requiring users to manually create and decorate a `JsonSerializerContext` with `[JsonSerializable(typeof(MyMessage))]` for every single message in their application is error-prone, noisy, and adds friction to adopting the library.

## Decision

Expand `MessagingIncrementalGenerator` to automatically generate a `GeneratedMessagingJsonSerializerContext` containing `[JsonSerializable]` attributes for all message contracts discovered across compile-time handlers:

```csharp
namespace EricksonLopez.Messaging.Generated
{
    [JsonSourceGenerationOptions(
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        GenerationMode = JsonSourceGenerationMode.Default)]
    [JsonSerializable(typeof(global::MyNamespace.OrderCreatedEvent))]
    [JsonSerializable(typeof(global::MyNamespace.ProcessPaymentCommand))]
    internal sealed partial class GeneratedMessagingJsonSerializerContext : JsonSerializerContext
    {
    }
}
```

The generated `AddGeneratedMessagingHandlers(this IServiceCollection services)` extension automatically registers `GeneratedMessagingJsonSerializerContext.Default` as an `IJsonTypeInfoResolver` in the DI container. `NativeAotJsonSerializer` combines this resolver with any user-provided resolvers, enabling seamless zero-reflection serialization out-of-the-box.

## Consequences
- **Zero-Configuration Native AOT**: End-users do not need to manually configure `JsonSerializerContext` or `JsonSourceGenerationOptions` for their messages.
- **Trimming Safety**: All message payload types and their properties are preserved by Roslyn source generation, guaranteeing 100% trim-safe serialization.
- **Performance**: Zero runtime reflection, zero boxing, and precomputed serialization metadata.
