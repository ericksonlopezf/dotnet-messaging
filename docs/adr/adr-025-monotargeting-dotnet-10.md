# ADR-025: Monotargeting .NET 10 for Production Libraries

## Status
Accepted — September 2026

## Date
2026-09-13

## Context
During initial planning and pre-1.0 documentation iterations, multi-targeting across `.NET 8`, `.NET 9`, and `.NET 10` was considered. However, maintaining multi-targeting introduces significant trade-offs:
1. **Conditional compilation complexity**: Preprocessor directives (`#if NET10_0_OR_GREATER`) pollute codebase readability, complicate test suites, and increase maintenance surface.
2. **Native AOT & Trimming friction**: .NET 10 introduces critical improvements in RyuJIT vectorization (AVX-512), trimming analyzers, and native AOT code generation that differ fundamentally from .NET 8.
3. **Zero-allocation performance**: .NET 10 provides first-class primitives in `System.Text.Json`, `System.Threading.Channels`, and memory management (`ReadOnlyMemory<byte>`, `IBufferWriter<byte>`) that permit true zero-allocation fast paths without runtime fallback shims.

## Decision
All production runtime packages in the `EricksonLopez.Messaging` ecosystem are **monotargeted to `.NET 10` (`net10.0`)**, configured globally via `Directory.Build.props`:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
</PropertyGroup>
```

The only exceptions are compiler tooling packages:
- `EricksonLopez.Messaging.Generators`: Targets `netstandard2.0` to run seamlessly inside the Roslyn 4.8+ compiler host and IDEs (Visual Studio, VS Code, Rider).
- `EricksonLopez.Messaging.Analyzers`: Targets `netstandard2.0` to run inside Roslyn analyzer hosts.

## Consequences
- **Positive**:
  - Unlocks full RyuJIT AVX-512 optimizations and Native AOT zero-warning compilation.
  - Eliminates multi-target CI matrix build overhead and conditional compilation complexity.
  - Guarantees modern runtime semantics for `ValueTask<Result>` functional dispatch.
- **Trade-offs**:
  - Consuming applications must target .NET 10 (`net10.0`) or higher to reference `EricksonLopez.Messaging` runtime packages.
  - Legacy applications on .NET 8 / .NET 9 cannot consume 1.0.0 packages without upgrading their project target framework.
- **Documentation Alignment**:
  - Documentation and `README.md` must state `.NET 10 (net10.0)` as the primary runtime target, rather than claiming active multi-targeting across older frameworks.
