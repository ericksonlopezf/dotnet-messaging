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
    end

    subgraph CI Quality Gate [ci.yml / dotnet-build-test.yml]
        Restore[dotnet restore] --> Build[dotnet build -c Release]
        Build --> Test[dotnet test with XPlat Coverage]
        Test --> Sonar[SonarCloud Static Analysis]
        Test --> Codecov[Upload Coverage to Codecov]
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
```

---

## 1. GitHub Actions Workflows

### 1.1 `ci.yml` (Continuous Integration)
- **Trigger**: Push or Pull Request to `main` and `develop` branches.
- **Role**: Entry-point orchestration invoking the reusable `dotnet-build-test.yml` workflow with required secrets (`SNK_KEY`, `CODECOV_TOKEN`, `SONAR_TOKEN`).

### 1.2 `dotnet-build-test.yml` (Reusable Build & Test)
- **Inputs**:
  - `dotnet-version`: .NET SDK version to use (default `"10.0.x"`).
  - `test-filter`: Optional test filter expression (e.g. `Category!=Integration`).
  - `test-project`: Optional path to a specific test project (leave empty to run all projects).
  - `upload-coverage`: Boolean flag to upload reports to Codecov (default `true`).
  - `artifact-name`: Name for the test results artifact (default `"test-results"`).
- **Secrets**: `SNK_KEY` (Strong Name Key), `CODECOV_TOKEN` (Codecov upload), `SONAR_TOKEN` (SonarCloud).
- **Steps**:
  1. Setup .NET 10.x SDK and Zulu OpenJDK 17 (for SonarScanner).
  2. Restore base64-encoded Strong Name Key (`SNK_KEY`) to `EricksonLopez.Messaging.snk`.
  3. Begin SonarCloud analysis session.
  4. Compile solution in `Release` configuration (`dotnet build EricksonLopez.Messaging.slnx`).
  5. Execute all automated tests with Coverlet code coverage collectors (`DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover,cobertura`).
  6. Finalize SonarCloud analysis.
  7. Upload test `.trx` results and push coverage to Codecov.

### 1.3 `mutation-testing.yml` (Stryker Mutation Quality Gate)
- **Trigger**: Weekly cron schedule (`0 4 * * 1` — Monday 04:00 UTC) and manual `workflow_dispatch`.
- **Strategy**: Matrix execution across all 11 packages:
  - `Core` (`stryker-config.json`)
  - `Abstractions` (`stryker-abstractions-config.json`)
  - `Analyzers` (`stryker-analyzers-config.json`)
  - `AzureServiceBus` (`stryker-azureservicebus-config.json`)
  - `Generators` (`stryker-generators-config.json`)
  - `OpenTelemetry` (`stryker-opentelemetry-config.json`)
  - `RabbitMQ` (`stryker-rabbitmq-config.json`)
  - `AwsSqs` (`stryker-awssqs-config.json`)
  - `Kafka` (`stryker-kafka-config.json`)
  - `Events` (`stryker-events-config.json`)
  - `Testing` (`stryker-testing-config.json`)
- **Reporting & Evaluation**:
  - Individual results recorded by `scripts/record-stryker-result.js`.
  - Aggregated consolidated gate evaluation via `actions/github-script` enforcing break threshold $\ge 95\%$.
  - Posts GitHub Commit Status `mutation-testing/stryker` against target commit SHA.

### 1.4 `publish.yml` (NuGet Pack & Release)
- **Trigger**: Push to tags `v*.*.*` or `workflow_dispatch` triggered automatically by `release-please.yml`.
- **Permissions**: `id-token: write` (OIDC), `contents: write` (Releases), `attestations: write` (Sigstore).
- **Steps**:
  1. Resolve version from dispatch input, git tag, or fallback to `Directory.Build.props`.
  2. Validate Stryker Mutation Quality Gate via `scripts/verify-mutation-gate.js`.
  3. Compile solution and execute pre-publish tests.
  4. Pack all 11 `.nupkg` and `.snupkg` packages into `./nupkgs`.
  5. Generate Sigstore Build Provenance Attestations (`actions/attest-build-provenance@v2`).
  6. Authenticate to NuGet.org via short-lived OIDC exchange (`NuGet/login@v1`).
  7. Push packages to `api.nuget.org` with `--skip-duplicate`.
  8. Create tagged GitHub Release.

### 1.5 `release-please.yml` (Automated Versioning)
- **Trigger**: Push to `main`.
- **Role**: Analyzes conventional commit history, manages Release PRs, updates versions, and triggers `publish.yml` upon merge.

---

## 2. Quality Gates & Thresholds

| Quality Gate | Tooling | Threshold / Policy | CI Enforcement |
| :--- | :--- | :--- | :--- |
| **Line Coverage** | Coverlet + Codecov | **100%** target | Verified in `dotnet-build-test.yml` |
| **Branch Coverage** | Coverlet | **100%** target | Verified in `dotnet-build-test.yml` |
| **Mutation Testing** | Stryker.NET | **High: 100%**, **Low: 98%**, **Break: 95%** | Enforced in `mutation-testing.yml` & `publish.yml` |
| **Static Analysis** | SonarCloud + Roslyn | Zero code smells, zero security hotspots | Enforced in `dotnet-build-test.yml` |
| **Compiler Warnings** | MSBuild Roslyn | `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` | Enforced on every build |
| **Trimming / AOT** | .NET SDK Analyzers | `<EnableTrimAnalyzer>`, `<IsAotCompatible>` | Enforced on every build |
| **Dependency Audits** | Dependabot | Weekly scanning for NuGet & GitHub Actions | Not yet configured (.github/dependabot.yml absent — see Technical Debt) |

---

## 3. Required GitHub Secrets

| Secret Name | Purpose | Workflows Used |
| :--- | :--- | :--- |
| `SNK_KEY` | Base64-encoded Strong Name Key (`.snk`) for assembly signing. | `ci.yml`, `publish.yml` |
| `CODECOV_TOKEN` | Token for uploading coverage reports to Codecov. | `ci.yml`, `dotnet-build-test.yml`, `publish.yml` |
| `SONAR_TOKEN` | Authentication token for SonarCloud static analysis. | `ci.yml`, `dotnet-build-test.yml` |
| `GITHUB_TOKEN` | GitHub Actions automated token for Releases and commit statuses. | `release-please.yml`, `publish.yml`, `mutation-testing.yml` |

---

## 4. Supply Chain Security Architecture

1. **Passwordless Publishing (NuGet OIDC)**:
   - Uses GitHub Actions OpenID Connect (OIDC) identity federation with NuGet.org, removing static API keys and eliminating credential leakage risks.
2. **Sigstore Build Provenance**:
   - Every published `.nupkg` is cryptographically attested to the exact GitHub Actions runner, workflow, commit SHA, and repository that built it.
3. **Assembly Strong Name Signing**:
   - Binaries are signed with a private Strong Name Key (`.snk`), verifying assembly identity and tamper-resistance across the .NET runtime.
4. **Reproducible Symbols**:
   - SourceLink metadata and `.snupkg` symbol packages are published alongside every release for source-level debugging.

---

## 5. Technical Debt

The following items were identified during the repository audit. They represent known gaps in the build and security infrastructure. See also [`docs/technical-debt.md`](technical-debt.md) for the full prioritized register.

| Item | Priority | Remediation |
| :--- | :---: | :--- |
| **No `.github/dependabot.yml`** | P1 | Add Dependabot config for `nuget` and `github-actions` ecosystems to automate NuGet and action pin updates. |
| **No `global.json`** | P2 | Add a `global.json` pinning the .NET SDK to `10.0.x` to enforce consistent local dev toolchain. |
| **No `NuGet.config`** | P3 | Add a `NuGet.config` restricting package sources to `api.nuget.org` for supply chain integrity. |
| **OpenTelemetry version divergence** | P2 | `OpenTelemetry` is `1.11.2` while `OpenTelemetry.Api` is `1.17.0`. Align to a single stable OTel SDK release. |
