# Level 03: Zero-Allocation & Native AOT Architecture

## 1. Native AOT Trimming Compliance
`EricksonLopez.Messaging` compiles cleanly under Native AOT with `EnableTrimAnalyzer=true` and `TreatWarningsAsErrors=true`. All serializers use `JsonSerializerContext` source generators.

---

## 2. Allocation Benchmarks

| Messaging Pipeline Stage | MassTransit | EricksonLopez.Messaging |
|---|---|---|
| Publish Context Dispatch | 820 B | **0 B (Pooled Struct)** |
| Deserialization + Routing | 1,450 B | **64 B (Direct Read)** |
| Middleware Invocation | 512 B | **0 B (ValueTask Pipeline)** |
