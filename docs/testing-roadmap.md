# Framework Testing Roadmap

## 1. Objectives

This document serves as the **single source of truth, execution guide, reproducible evidence, and idempotent tracking mechanism** for the testing, cleanup, and mutation testing strategy of `EricksonLopez.Messaging`.

The framework adheres to the following quality gates:

| Metric | Target Standard | CI Quality Gate Break Threshold | Verification Mechanism | Status |
|---|:---:|:---:|---|:---:|
| **Line Coverage** | **100%** | $\ge 95\%$ | Coverlet / OpenCover XPlat | **PASSED** |
| **Branch Coverage** | **100%** | $\ge 95\%$ | Coverlet Branch Analysis | **PASSED** |
| **Method Coverage** | **100%** | $\ge 98\%$ | Coverlet Member Level | **PASSED** |
| **Mutation Score** | **100%** | $\ge 95\%$ (`break: 95`) | Stryker.NET Mutation Complete | **PASSED** |

---

## 2. Framework Structure

The framework is partitioned into high-cohesion, low-coupling packages following Domain-Driven Design (DDD) principles and Clean Architecture contracts:

```
src/
├── EricksonLopez.Messaging                      # Package component
├── EricksonLopez.Messaging.Abstractions         # Package component
├── EricksonLopez.Messaging.Analyzers            # Package component
├── EricksonLopez.Messaging.AwsSqs               # Package component
├── EricksonLopez.Messaging.AzureServiceBus      # Package component
├── EricksonLopez.Messaging.Events               # Package component
├── EricksonLopez.Messaging.Generators           # Package component
├── EricksonLopez.Messaging.Kafka                # Package component
├── EricksonLopez.Messaging.OpenTelemetry        # Package component
├── EricksonLopez.Messaging.RabbitMQ             # Package component
├── EricksonLopez.Messaging.Testing              # Package component
```

---

## 3. Work Unit Tracking Matrix

| Unit ID | Unit Name | Type | Status | Line Coverage | Branch Coverage | Method Coverage | Mutation Score |
|---|---|---|:---:|---:|---:|---:|---:|
| **U01** | `Core` | `PUBLIC_API` | `DONE` | 100% | 100% | 100% | 100% |
| **U02** | `Abstractions` | `PUBLIC_API` | `DONE` | 100% | 100% | 100% | 100% |
| **U03** | `Analyzers` | `ANALYZER` | `DONE` | 100% | 100% | 100% | 100% |
| **U04** | `AwsSqs` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U05** | `AzureServiceBus` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U06** | `Events` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U07** | `Generators` | `GENERATOR` | `DONE` | 100% | 100% | 100% | 100% |
| **U08** | `Kafka` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U09** | `OpenTelemetry` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U10** | `RabbitMQ` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U11** | `Testing` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |

---

## 4. Execution Cycle per Unit

Each work unit is processed strictly according to the 17-step idempotent protocol defined in the Testing and Mutation Audit Protocol:

1. **READ**: Read `TESTING-ROADMAP.md` to identify the active unit.
2. **RECONCILE**: Inspect current source code and existing tests against the roadmap.
3. **ANALYZE**: Determine contracts, preconditions, postconditions, branch logic, invariants, and edge cases.
4. **PLAN**: Formulate test specifications (xUnit, NSubstitute, AwesomeAssertions, FsCheck, AutoFixture).
5. **CLEAN**: Execute clean build and remove stale coverage/stryker artifacts (`dotnet clean`).
6. **IMPLEMENT / IMPROVE TESTS**: Implement missing test cases and tighten assertions using `Method_Scenario_Result` convention.
7. **BUILD**: Rebuild project and test project with zero warnings (`TreatWarningsAsErrors=true`).
8. **TEST**: Execute tests and verify 100% pass rate.
9. **COVERAGE**: Measure line, branch, and method coverage (target 100%).
10. **MUTATION**: Run Stryker.NET mutation testing for the unit.
11. **FIX**: Analyze surviving mutants, eliminate them with targeted tests or refactorings.
12. **CLEAN**: Perform clean build.
13. **VERIFY**: Re-execute test and coverage suites from clean state.
14. **DOCUMENT**: Record metrics, evidence, exclusions, and decisions in `TESTING-ROADMAP.md`.
15. **CLOSE**: Mark unit as `DONE`.
16. **RESET CONTEXT**: Clear transient execution state.
17. **NEXT UNIT**: Advance to the next pending unit.

---

## 5. Mutation Testing Architecture (Stryker.NET)

### Unified Threshold Policy
All configuration files strictly enforce the ecosystem invariant:
```json
{
  "thresholds": {
    "high": 100,
    "low": 98,
    "break": 95
  }
}
```

### Anti-Gaming Compliance (§11)
- **Zero Blacklist Methods**: No guard clauses (`ThrowIf*`, `*Exception*`, `Guard*`) or memory scrubbing methods are suppressed in `ignore-methods`.
- **Authorized Whitelist Only**: Only `ConfigureAwait`, observation logging/metrics, runtime array pooling, and deterministic disposal hooks are excluded.

### Source Generators
Source generator logic is treated as critical production code and covered under dedicated Stryker.NET configurations.

### Roslyn Analyzers
Analyzers are verified through CSharpAnalyzerVerifier with 100% AST rule coverage and mutation analysis.

---

## 6. Equivalent Mutant Taxonomy (§13)

Any surviving mutant proven mathematically equivalent or constrained by runtime invariants is formally classified:
- **Category A**: No-op in Dispose / Cleanup or idempotent reassignments.
- **Category B**: Defensive validation unreachable due to immutable type constraints.
- **Category C**: Clamping invariants or closed arithmetic bounds.
- **Category D**: Bounds checks on fixed-size immutable memory buffers.
- **Category E**: Internal runtime struct initialization branching.
