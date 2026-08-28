# ADR-020: Roslyn Analyzers ELMSG004 and ELMSG010 — Handler Lifetime and Return Type Enforcement

## Status
Accepted — August 2026

## Context
The functional parity audit (August 2026) identified **GAP-07**: diagnostic descriptors `ELMSG004` and `ELMSG010` were defined in `DiagnosticDescriptors.cs` but lacked corresponding `DiagnosticAnalyzer` implementations. They existed as documentation intent without compile-time enforcement.

### ELMSG004 — Handler Lifetime
`IMessageHandler<T>` implementations must be registered as **Scoped** lifetime in the DI container. Each message dispatch creates an isolated `IServiceScope` (via `IServiceScopeFactory`) to ensure handler state is not shared across concurrent message processing. Registering a handler as `Singleton` or `Transient` breaks this invariant:
- **Singleton**: Shared mutable state across all messages and threads — data corruption and race conditions.
- **Transient**: New instance per injection point within a scope — but the scope is per-message, so a `Transient` handler inside a Scoped context behaves equivalently to Scoped. However, if resolved outside the scope (e.g., directly from the root container), state isolation is lost.

The root concern is **accidental registration** using `services.AddSingleton<MyHandler>()` or `services.AddTransient<MyHandler>()`.

### ELMSG010 — Return Type
`IMessageHandler<T>.HandleAsync` must return `ValueTask<Result>`. The EricksonLopez.Messaging framework is built on the **Result pattern** (ADR-008) as its primary error-handling contract. A handler returning `Task<Result>`, `void`, `bool`, or any other type:
1. Breaks the `IMessageHandler<T>` interface contract.
2. Cannot integrate with the middleware pipeline (`MiddlewarePipeline` chains `ValueTask<Result>`).
3. May cause silently wrong behavior — the compiler may generate implicit conversion warnings rather than errors.

## Decision

### ELMSG004 — `InvalidHandlerLifetimeAnalyzer`

**Approach**: Syntax-level analysis of `InvocationExpressionSyntax` nodes. The analyzer scans for calls to `AddSingleton`, `AddTransient`, `TryAddSingleton`, `TryAddTransient` where:
- A generic type argument resolves to a type implementing `IMessageHandler<T>` (generic registration), OR
- A `typeof(...)` argument resolves to such a type.

**Why syntax-level?** The forbidden registration methods are always called on `IServiceCollection` — the type resolution via semantic model is sufficient to distinguish handler types from other services. Syntactic inspection of method name is a fast first filter before semantic lookup.

**Severity**: `Error` — prevents accidental lifetime misconfiguration from reaching production. Force-fixing at compile time is vastly cheaper than debugging DI-related race conditions at runtime.

**Out of scope**: The analyzer does not flag code paths that register handlers via `AddMessageHandler<TMsg, THandler>()` (the framework's own extension method), since that always uses `AddScoped`.

---

### ELMSG010 — `HandlerMustReturnResultAnalyzer`

**Approach**: Symbol-level analysis (`SymbolKind.NamedType`). For each class or struct that implements `IMessageHandler<T>` (transitively via `AllInterfaces`), the analyzer inspects every `HandleAsync` method to verify its return type is exactly `ValueTask<Result>`.

**Verification logic**:
1. The return type must be `INamedTypeSymbol` (generic type).
2. The constructed-from display string must start with `System.Threading.Tasks.ValueTask`.
3. It must have exactly one type argument.
4. That type argument's display string must equal `EricksonLopez.Result.Result` OR its `Name` must be `"Result"` (to handle cases where the full namespace is not present in the compilation context).

**Severity**: `Error` — returns `Task<Result>`, `void`, or other types are rejected at compile time. This enforces the Result pattern contract consistently across the entire handler surface.

---

### Why not a single combined analyzer?
Each rule has a distinct analysis entry point (`SyntaxNodeAction` for ELMSG004, `SymbolAction` for ELMSG010) and distinct concerns. Separating them keeps each analyzer focused, testable independently, and composable in code analysis pipelines.

## Alternatives Considered

### Alternative A: Suppress ELMSG004 / document as a guideline only
**Rejected.** The functional parity audit explicitly listed the lack of implementation as a medium-impact false parity finding (FP-02). If the descriptor exists but is not enforced, it creates a misleading false sense of safety. Either remove the descriptor or implement the analyzer.

### Alternative B: ELMSG004 using symbol-level type hierarchy analysis instead of syntax
**Considered.** Walking the full inheritance/interface chain at the `INamedType` symbol level would catch cases like `AddSingleton<IFoo, FooHandler>()` where `FooHandler : IMessageHandler<T>` is resolved through an interface. However, this also increases false-positive risk for complex DI patterns. The current syntax + semantic hybrid approach balances precision and performance.

### Alternative C: ELMSG010 applied to interface declaration, not implementation
**Rejected.** `IMessageHandler<T>` is declared in the core contracts package with the correct return type. The analyzer targeting the interface would be redundant (the interface already enforces the contract). The correct target is implementations that attempt to override `HandleAsync` with an incompatible return type, which is what the symbol-level analysis catches.

## Consequences

- **Positive**: `ELMSG004` closes the compile-time gap for DI lifetime misconfiguration — a category of bugs that is silent and difficult to reproduce under load.
- **Positive**: `ELMSG010` enforces the Result pattern contract as a compile-time contract, preventing accidental `Task<Result>` or `void` returns that could silently bypass the middleware pipeline.
- **Positive**: Both analyzers are incremental-analyzer-compatible and do not impact build throughput.
- **Positive**: Full unit test coverage via `Microsoft.CodeAnalysis.Testing` in `EricksonLopez.Messaging.Analyzers.Tests`.
- **Trade-off**: `ELMSG004` operates on syntax (method name string matching before semantic lookup). An adversarial developer could introduce a local extension method named `AddSingleton` that is not DI-related — but this is a negligible edge case in practice.

## References
- ADR-008 — Result pattern integration (`docs/adr/adr-008-result-pattern-integration.md`)
- `InvalidHandlerLifetimeAnalyzer.cs` — `src/EricksonLopez.Messaging.Analyzers/Analyzers/InvalidHandlerLifetimeAnalyzer.cs`
- `HandlerMustReturnResultAnalyzer.cs` — `src/EricksonLopez.Messaging.Analyzers/Analyzers/HandlerMustReturnResultAnalyzer.cs`
- `DiagnosticDescriptors.cs` — `src/EricksonLopez.Messaging.Analyzers/Rules/DiagnosticDescriptors.cs`
- `AnalyzerUnitTests.cs` — `tests/EricksonLopez.Messaging.Analyzers.Tests/AnalyzerUnitTests.cs`
- Functional parity audit GAP-07 / FP-02
