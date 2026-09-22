# Level 03: Zero-Allocation & Native AOT Architecture

## 1. Native AOT Trimming Compliance

`EricksonLopez.Messaging` compiles cleanly under Native AOT with `<IsAotCompatible>true</IsAotCompatible>`, `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>`, and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.

To achieve full trimming and AOT compatibility, the framework eliminates all runtime reflection:
- **Compile-Time Handler Discovery**: `MessagingIncrementalGenerator` scans `IMessageHandler<TMessage>` implementations at compile time and emits strongly-typed invoker delegates:
  ```csharp
  (sp, msg, ctx, ct) => sp.GetRequiredService<THandler>().HandleAsync((TMessage)msg, ctx, ct);
  ```
- **Source-Generated JSON Serialization**: `MessagingIncrementalGenerator` emits a dedicated `JsonSerializerContext` registering every discovered `IMessage` contract.
- **Zero Runtime `AppDomain.GetAssemblies()`**: Handlers are registered either explicitly or via source-generated extension methods, avoiding `IL2026` and `IL3050` trimming warnings.

---

## 2. Allocation Optimization on Hot Paths

The message dispatch pipeline is engineered to minimize heap allocations during message publication, transport relay, and handler execution:

| Pipeline Stage | Optimization Strategy | Allocation Profile |
|---|---|---|
| **Handler Return Type** | `ValueTask<Result>` instead of `Task<Result>` | **0 B** on synchronous completion |
| **Serialization Buffers** | `IBufferWriter<byte>` direct write into shared buffer | **0 B** intermediate array copies |
| **Transport Delivery** | `ReadOnlyMemory<byte>` slices passed to transport callback | **0 B** heap allocation |
| **Batch Dispatch** | `MessageDispatchItem` readonly struct encapsulation | **0 B** per message item |
| **Deduplication Store** | `InMemoryMessageDeduplicationStore` with struct entries | Bounded sliding window memory |

---

## 3. Verifying Native AOT in CI

The repository validates Native AOT compilation using the dedicated project `tests/EricksonLopez.Messaging.AotSmokeTest/`.

The CI workflow `.github/workflows/aot-smoke-test.yml` executes:

```bash
dotnet publish tests/EricksonLopez.Messaging.AotSmokeTest/EricksonLopez.Messaging.AotSmokeTest.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained \
  -p:PublishAot=true \
  -p:TreatWarningsAsErrors=true \
  -o ./aot-output

./aot-output/EricksonLopez.Messaging.AotSmokeTest
```

Any trimmer warning (`IL2026`, `IL3050`) causes immediate build failure under `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
