# Build, CI/CD, and Quality Engineering Guide

This document describes the automated continuous integration, continuous delivery (CI/CD), quality gates, and supply chain security architecture implemented in `EricksonLopez.Messaging`.

---

## CI/CD Pipeline Architecture

```mermaid
flowchart TD
    subgraph Trigger
        PushPR[Push or Pull Request to main / develop]
        ReleasePR[Merged Release Please PR]
        ManualTag[Push Git Tag v*.*.*]
        Scheduled[Cron Schedule / Dispatch]
    end

    subgraph CI Quality Gate [ci.yml]
        direction TB
        BldTest[dotnet-build-test.yml\nRestore -> Build -> Test -> SonarCloud -> Codecov]
        AotSmoke[aot-smoke-test.yml\nPublishAot=true -> Execute Native Binary]
        Compliance[repo-compliance.yml\nverify-compliance.ps1 Architecture & Invariants]
        BenchGate[benchmark-regression-gate.yml\n0 B Allocation & <= 5% Latency Deviation]
    end

    subgraph Mutation Quality Gate [mutation-testing.yml]
        StrykerMatrix[Stryker.NET Matrix across 11 packages]
        StrykerMatrix --> EvalGate[Evaluate Consolidated Mutation Gate\nbreak threshold >= 95%]
        EvalGate --> Status[Post GitHub Commit Status]
    end

    subgraph Release Pipeline [publish.yml]
        VerifyGate[Validate Stryker Quality Gate] --> Pack[dotnet pack all 11 packages]
        Pack --> Sigstore[Sigstore Provenance Attestation]
        Sigstore --> OIDC[NuGet.org Push via OIDC]
        OIDC --> GHRelease[Create GitHub Release]
    end

    PushPR --> CI Quality Gate
    ReleasePR --> Release Pipeline
    ManualTag --> Release Pipeline
    Scheduled --> Mutation Quality Gate
```

---

## 1. GitHub Actions Workflows Inventory

The repository defines 10 specialized GitHub Actions workflows located in `.github/workflows/`:

| Workflow File | Name | Trigger | Primary Responsibility |
| :--- | :--- | :--- | :--- |
| `ci.yml` | Continuous Integration | `push`, `pull_request` (`main`, `develop`) | Master CI orchestrator running build, test, coverage, and Native AOT smoke validation. |
| `dotnet-build-test.yml` | Reusable Build & Test | `workflow_call` | Restores SNK key, builds solution (`Release`), executes test suites with Coverlet, uploads to SonarCloud and Codecov. |
| `aot-smoke-test.yml` | NativeAOT Smoke Test | `push`, `pull_request`, `workflow_call`, `workflow_dispatch` | Publishes and executes native binary with `PublishAot=true` and zero trimming warnings. |
| `benchmark-regression-gate.yml` | Benchmark Regression Gate | `pull_request` (`main`, `develop`), `workflow_dispatch` | Enforces zero heap allocations (0 B) and $\le 5\%$ latency regression on performance-critical paths. |
| `benchmarks.yml` | Benchmarks | `workflow_call`, `workflow_dispatch` | Captures baseline BenchmarkDotNet performance metrics. |
| `weekly-benchmarks.yml` | Weekly Benchmarks | Schedule (`0 2 * * 0` — Sunday 02:00 UTC), `workflow_dispatch` | Deep performance profiling and regression tracking across .NET 10 runtime. |
| `mutation-testing.yml` | Stryker Mutation Testing | Schedule (`0 4 * * 1` — Monday 04:00 UTC), `workflow_dispatch` | Matrix execution of Stryker.NET across all 11 packages with consolidated gate evaluation. |
| `publish.yml` | Publish NuGet Packages | Push tags `v*.*.*`, `workflow_dispatch` | Packs 11 signed packages, verifies Sigstore provenance attestations, publishes to NuGet.org via OIDC, and drafts GitHub Releases. |
| `release-please.yml` | Release Please | `push` (`main`) | Evaluates Conventional Commits, maintains release PRs, generates CHANGELOG entries, and dispatches `publish.yml`. |
| `repo-compliance.yml` | Repository Compliance | `push`, `pull_request` (`main`), `workflow_dispatch` | Validates file naming conventions, clean code invariants, and documentation consistency via `scripts/verify-compliance.ps1`. |

---

## 2. Quality Gates & Thresholds

| Quality Gate | Tooling | Threshold / Policy | CI Enforcement |
| :--- | :--- | :--- | :--- |
| **Line Coverage** | Coverlet + Codecov | **100%** target | Enforced in `dotnet-build-test.yml` |
| **Branch Coverage** | Coverlet + Codecov | **100%** target | Enforced in `dotnet-build-test.yml` |
| **Mutation Testing** | Stryker.NET | **High: 100%**, **Low: 98%**, **Break: 95%** | Enforced in `mutation-testing.yml` & `publish.yml` |
| **Benchmark Invariants** | BenchmarkDotNet | **0 B Heap Allocation**, $\le 5\%$ Latency Regression | Enforced in `benchmark-regression-gate.yml` |
| **Static Analysis** | SonarCloud + Roslyn | Zero code smells, zero security hotspots | Enforced in `dotnet-build-test.yml` |
| **Compiler Warnings** | MSBuild Roslyn | `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` | Enforced across all projects |
| **Trimming / AOT** | .NET Trimmer & Native AOT | Zero IL2026/IL3050 warnings; `<IsAotCompatible>true</IsAotCompatible>` (except Kafka) | Enforced in `aot-smoke-test.yml` |
| **Dependency Audits** | GitHub Dependabot | Weekly scanning for NuGet packages and GitHub Actions | Enforced via `.github/dependabot.yml` |
| **Repository Compliance**| PowerShell Invariant Script | 100% compliance across file casing, licensing, and architecture rules | Enforced in `repo-compliance.yml` |

---

## 3. Required GitHub Secrets

| Secret Name | Purpose | Workflows Used |
| :--- | :--- | :--- |
| `SNK_KEY` | Base64-encoded Strong Name Key (`.snk`) for assembly signing. | `ci.yml`, `dotnet-build-test.yml`, `publish.yml` |
| `CODECOV_TOKEN` | Authentication token for uploading coverage reports to Codecov. | `ci.yml`, `dotnet-build-test.yml`, `publish.yml` |
| `SONAR_TOKEN` | Authentication token for SonarCloud static code analysis. | `ci.yml`, `dotnet-build-test.yml` |
| `GITHUB_TOKEN` | Automatic GitHub token with scoped permissions for Releases, commit statuses, and PR checks. | `release-please.yml`, `publish.yml`, `mutation-testing.yml`, `repo-compliance.yml`, `benchmark-regression-gate.yml` |

---

## 4. Supply Chain Security Architecture

1. **Passwordless Publishing (NuGet OIDC)**:
   - Uses GitHub Actions OpenID Connect (OIDC) identity federation with NuGet.org (`NuGet/login@v1`), eliminating long-lived API tokens and credential exposure risks.
2. **Sigstore Build Provenance**:
   - Every published `.nupkg` is cryptographically attested to the exact runner, workflow, commit SHA, and repository via `actions/attest-build-provenance@v2`.
3. **Assembly Strong Name Signing**:
   - All production binaries are signed with a private Strong Name Key (`EricksonLopez.snk`), verifying assembly identity and tamper-resistance.
4. **Reproducible Symbols**:
   - SourceLink metadata and `.snupkg` symbol packages are generated and published alongside every release for deterministic source debugging.
5. **Feed Isolation & Central Package Management (CPM)**:
   - `NuGet.config` locks package restoration to `https://api.nuget.org/v3/index.json`. CPM in `Directory.Packages.props` prevents package version drift across projects.

---

## 5. Technical Debt Status

The technical debt register is formally tracked in [`docs/technical-debt.md`](technical-debt.md). Summary of items audited:

| Item | Priority | Category | Status |
| :--- | :---: | :--- | :---: |
| **TD-001 — No `.github/dependabot.yml`** | P1 | Supply Chain Security | ✅ **Remediated** |
| **TD-002 — No `global.json`** | P2 | Developer Experience / Reproducibility | ✅ **Remediated** |
| **TD-003 — No `NuGet.config`** | P3 | Supply Chain Security | ✅ **Remediated** |
| **TD-004 — OpenTelemetry CPM Version Divergence** | P2 | Dependency Management | ✅ **Remediated** |
| **TD-005 — Static Badges in README** | P2 | Documentation Accuracy | ✅ **Remediated** |
| **TD-006 — Conventional Git Commit History** | P1 | Release Automation | ⚠️ **Tracked for Release** |
| **TD-007 — Kafka Transport AOT Compatibility Flag** | P2 | Native AOT Compatibility | ✅ **Remediated** |
