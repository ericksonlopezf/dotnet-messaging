# Technical Debt Register — EricksonLopez.Messaging

This document tracks known technical debt items identified during the repository audit. Each item includes a severity classification, root cause, impact, and recommended remediation path. Items are ordered by priority.

> Items in this register are derived exclusively from the code, configurations, and tooling present in the repository. No speculative debt is included.

---

## TD-001 — No `.github/dependabot.yml` (Dependency Scanning)

| Field | Value |
| :--- | :--- |
| **Priority** | P1 (High) |
| **Category** | Supply Chain Security |
| **Discovered** | Repository audit — `.github/` directory inspection |

### Description
No Dependabot configuration exists in the repository. This means NuGet package upgrades and GitHub Actions pin updates are not automated and require manual tracking.

### Impact
- NuGet dependencies (e.g. `RabbitMQ.Client`, `Azure.Messaging.ServiceBus`, `Confluent.Kafka`, `AWSSDK.SQS`) may fall behind on security patches.
- GitHub Actions (`actions/checkout@v4`, `actions/setup-dotnet@v4`, etc.) are not automatically updated when new action versions are released.

### Remediation
Create `.github/dependabot.yml`:

```yaml
version: 2
updates:
  - package-ecosystem: "nuget"
    directory: "/"
    schedule:
      interval: "weekly"
      day: "monday"
    open-pull-requests-limit: 5

  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
      day: "monday"
    open-pull-requests-limit: 5
```

---

## TD-002 — No `global.json` (SDK Version Pinning)

| Field | Value |
| :--- | :--- |
| **Priority** | P2 (Medium) |
| **Category** | Developer Experience / Reproducibility |
| **Discovered** | Repository root inspection |

### Description
The repository does not include a `global.json` file to pin the .NET SDK version locally. The SDK version is only pinned in the CI reusable workflow (`dotnet-version: "10.0.x"`), which means developers using a different installed major SDK version may encounter build inconsistencies.

### Impact
- Local builds by contributors with a different SDK major version may silently use a different compiler and produce different warnings or code gen outputs.
- `TreatWarningsAsErrors=true` combined with a future compiler may introduce build breaks not caught until CI.

### Remediation
Add `global.json` at the repository root:

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestMinor"
  }
}
```

---

## TD-003 — No `NuGet.config` (Package Source Restriction)

| Field | Value |
| :--- | :--- |
| **Priority** | P3 (Low) |
| **Category** | Supply Chain Security |
| **Discovered** | Repository root inspection |

### Description
No `NuGet.config` exists to restrict package restoration to the official `api.nuget.org` source. Without this, a local developer with a misconfigured NuGet.config (e.g. pointing to a corporate feed with namespace collisions) could silently resolve packages from unintended sources.

### Remediation
Add `NuGet.config` at the repository root:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

---

## TD-004 — OpenTelemetry Version Divergence in CPM

| Field | Value |
| :--- | :--- |
| **Priority** | P2 (Medium) |
| **Category** | Dependency Management / Reliability |
| **Discovered** | `Directory.Packages.props` analysis |

### Description
The OpenTelemetry ecosystem packages are pinned at two different versions in `Directory.Packages.props`:

| Package | Pinned Version |
| :--- | :--- |
| `OpenTelemetry` | `1.11.2` |
| `OpenTelemetry.Exporter.Console` | `1.11.2` |
| `OpenTelemetry.Api` | `1.17.0` |
| `OpenTelemetry.Extensions.Hosting` | `1.17.0` |

### Impact
Mixed releases within the same SDK family can cause assembly binding conflicts. The OTel project recommends using a unified version across all `OpenTelemetry.*` packages in the same application.

### Remediation
Align all OTel packages to the same stable version. Update `OpenTelemetry.Api` and `OpenTelemetry.Extensions.Hosting` from `1.17.0` to `1.11.2`, or upgrade all four packages to the latest unified GA release.

---

## TD-005 — Static CI/Quality Badges in README

| Field | Value |
| :--- | :--- |
| **Priority** | P2 (Medium) |
| **Category** | Developer Experience / Documentation Accuracy |
| **Status** | ✅ Remediated in this audit |

### Description
The README previously contained static `img.shields.io/badge` URLs hardcoded to `100%` for Codecov and Stryker coverage, which did not reflect the live repository state.

**Remediation applied**: Replaced with a live Codecov badge (`codecov.io/gh/ericksonlopezf/dotnet-messaging/graph/badge.svg`) and a live GitHub Actions workflow status badge for mutation testing.

---

## TD-006 — Unconventional Git Commit History

| Field | Value |
| :--- | :--- |
| **Priority** | P1 (High) |
| **Category** | Release Automation / Auditability |
| **Discovered** | `git log --oneline` analysis |

### Description
All git commits in the repository use the message "asdf", which:
1. Violates the [Conventional Commits](https://www.conventionalcommits.org/) standard required by Release Please.
2. Prevents `release-please.yml` from detecting `feat:`, `fix:`, or other semantic prefixes.
3. Means no git tags exist (`git tag --list` returns empty), so `v1.0.0` exists only in `.release-please-manifest.json` and `Directory.Build.props` — not as a verified git tag.

### Impact
- Automated release PRs from Release Please will not be created without at least one Conventional Commit since the last tag.
- The `[1.0.0] - 2026-08-21` CHANGELOG entry cannot be verified against a git tag.

### Remediation
- Going forward, all commits must follow Conventional Commits: `feat:`, `fix:`, `docs:`, `chore:`, etc.
- Tag `v1.0.0` manually against HEAD when ready: `git tag v1.0.0 && git push origin v1.0.0`.

---

## TD-007 — Kafka Transport Suppresses AOT Trimming Warnings

| Field | Value |
| :--- | :--- |
| **Priority** | P2 (Medium) |
| **Category** | Native AOT Compatibility |
| **Discovered** | `EricksonLopez.Messaging.Kafka.Tests.csproj` `<NoWarn>` analysis |

### Description
The Kafka test project suppresses trimming diagnostic warnings `IL2072`, `IL2026`, and `IL2075`. These indicate code paths potentially incompatible with Native AOT trimming, likely originating from `Confluent.Kafka`.

### Impact
The `EricksonLopez.Messaging.Kafka` package claims `IsAotCompatible=true` (inherited from `Directory.Build.props`), but the test-level suppressions suggest unresolved AOT boundary issues at the `Confluent.Kafka` SDK level.

### Remediation
1. Trace each `IL` suppression to its origin (transport adapter vs. SDK).
2. If `Confluent.Kafka` is not AOT-compatible, set `<IsAotCompatible>false</IsAotCompatible>` in `EricksonLopez.Messaging.Kafka.csproj` and update the AOT compatibility matrix in `docs/public-api-reference.md`.
3. Open a tracking issue with the `Confluent.Kafka` maintainers for Native AOT support.
