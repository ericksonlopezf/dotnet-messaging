# Technical Debt Register — EricksonLopez.Messaging

This document tracks technical debt items identified during the repository audit. Each item includes a severity classification, root cause, impact, status, and remediation details.

---

## TD-001 — No `.github/dependabot.yml` (Dependency Scanning)

| Field | Value |
| :--- | :--- |
| **Priority** | P1 (High) |
| **Category** | Supply Chain Security |
| **Status** | ✅ **Remediated** |

### Description
No Dependabot configuration existed in the repository.

### Remediation Applied
Created `.github/dependabot.yml` configured for weekly updates of `nuget` and `github-actions`.

---

## TD-002 — No `global.json` (SDK Version Pinning)

| Field | Value |
| :--- | :--- |
| **Priority** | P2 (Medium) |
| **Category** | Developer Experience / Reproducibility |
| **Status** | ✅ **Remediated** |

### Description
The repository did not pin the .NET SDK version locally.

### Remediation Applied
Added `global.json` pinning `10.0.100` with `latestFeature` roll-forward.

---

## TD-003 — No `NuGet.config` (Package Source Restriction)

| Field | Value |
| :--- | :--- |
| **Priority** | P3 (Low) |
| **Category** | Supply Chain Security |
| **Status** | ✅ **Remediated** |

### Description
No `NuGet.config` existed to restrict package restoration to official feeds.

### Remediation Applied
Added `NuGet.config` at the repository root locking package resolution to `https://api.nuget.org/v3/index.json`.

---

## TD-004 — OpenTelemetry Version Divergence in CPM

| Field | Value |
| :--- | :--- |
| **Priority** | P2 (Medium) |
| **Category** | Dependency Management / Reliability |
| **Status** | ✅ **Remediated** |

### Description
OpenTelemetry packages were pinned at two different versions in `Directory.Packages.props` (`1.11.2` vs `1.17.0`).

### Remediation Applied
Aligned all `OpenTelemetry.*` packages to `1.11.2` in `Directory.Packages.props`.

---

## TD-005 — Static CI/Quality Badges in README

| Field | Value |
| :--- | :--- |
| **Priority** | P2 (Medium) |
| **Category** | Developer Experience / Documentation Accuracy |
| **Status** | ✅ **Remediated** |

### Description
README previously contained hardcoded static badges.

### Remediation Applied
Replaced with live Codecov and GitHub Actions status badges.

---

## TD-006 — Unconventional Git Commit History

| Field | Value |
| :--- | :--- |
| **Priority** | P1 (High) |
| **Category** | Release Automation / Auditability |
| **Status** | ⚠️ **Tracked for Release** |

### Description
Commits require Conventional Commits format (`feat:`, `fix:`) for Release Please automation.

### Remediation
Enforce Conventional Commits on subsequent PR merges and tag `v1.0.0` upon release.

---

## TD-007 — Kafka Transport Suppresses AOT Trimming Warnings

| Field | Value |
| :--- | :--- |
| **Priority** | P2 (Medium) |
| **Category** | Native AOT Compatibility |
| **Status** | ✅ **Remediated** |

### Description
`EricksonLopez.Messaging.Kafka` inherited `IsAotCompatible=true` despite `Confluent.Kafka` C-interop.

### Remediation Applied
Explicitly set `<IsAotCompatible>false</IsAotCompatible>` in `EricksonLopez.Messaging.Kafka.csproj`.
