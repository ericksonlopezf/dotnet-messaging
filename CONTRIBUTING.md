# Contributing to EricksonLopez.Messaging

Thank you for your interest in contributing to `EricksonLopez.Messaging`. This document outlines the development workflow, quality gates, and contribution guidelines.

---

## Prerequisites

- **.NET SDK**: `.NET 10.0 SDK`. No `global.json` is present — the CI workflow pins `dotnet-version: "10.0.x"`. Ensure your local SDK major version matches `net10.0` (the target framework set in `Directory.Build.props`).
- **C# Language Version**: `latest` (resolves to **C# 13** when targeting `net10.0`), as set in `Directory.Build.props`.
- **Node.js**: `18.x+` (required for Stryker gate validation scripts in `scripts/`).
- **Docker**: (Optional) For running local broker integration tests (RabbitMQ, Kafka, LocalStack).
- **IDE**: Visual Studio 2022 (v17.12+), JetBrains Rider 2024.3+, or Visual Studio Code with the C# Dev Kit.

---

## Build & Test Commands

### 1. Restore & Build

```bash
# Restore dependencies across the solution
dotnet restore EricksonLopez.Messaging.slnx

# Build in Release mode with all warnings treated as errors
dotnet build EricksonLopez.Messaging.slnx --configuration Release -p:TreatWarningsAsErrors=true
```

### 2. Run Test Suites

All test methods adhere to the **Roy Osherove pattern** (`[UnitOfWork]_[StateUnderTest]_[ExpectedBehavior]`) per [ADR-015](docs/adr/adr-015-test-naming-osherove-ide1006.md).

```bash
# Run all tests across the solution
dotnet test EricksonLopez.Messaging.slnx --configuration Release

# Run tests by category filter
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Transport"
dotnet test --filter "Category=Concurrency"
dotnet test --filter "Category=Roslyn"
dotnet test --filter "Category=Integration"
```

### 3. Run Mutation Testing (Stryker.NET)

Stryker mutation testing runs across all 11 packages to ensure high test determinism.

```bash
# Install dotnet-stryker globally if not already installed
dotnet tool install --global dotnet-stryker

# Run Stryker on Core
dotnet stryker --config-file stryker-config.json

# Run Stryker on individual packages (one config file per package)
dotnet stryker --config-file stryker-abstractions-config.json
dotnet stryker --config-file stryker-analyzers-config.json
dotnet stryker --config-file stryker-generators-config.json
dotnet stryker --config-file stryker-azureservicebus-config.json
dotnet stryker --config-file stryker-rabbitmq-config.json
dotnet stryker --config-file stryker-awssqs-config.json
dotnet stryker --config-file stryker-kafka-config.json
dotnet stryker --config-file stryker-opentelemetry-config.json
dotnet stryker --config-file stryker-events-config.json
dotnet stryker --config-file stryker-testing-config.json
```

#### Mutation Gate Thresholds
- **High (Green)**: $\ge 100\%$
- **Low (Yellow)**: $\ge 98\%$
- **Warning (Orange)**: $\ge 95\%$
- **Break (Failure)**: $< 95\%$

---

## Development & Code Standards

1. **Native AOT & Zero-Reflection**:
   - `IsAotCompatible` is enabled for all library projects.
   - Do not use runtime reflection or dynamic type invocation in dispatch or hot paths. Use Roslyn Incremental Generators (`MessagingIncrementalGenerator`) and `JsonSerializerContext`.
2. **Functional Error Handling**:
   - Handlers must return `ValueTask<Result>` (`EricksonLopez.Result.Result`), enforced at compile time by analyzer `ELMSG010`.
   - Avoid throwing exceptions for control flow.
3. **Nullability & Compiler Warnings**:
   - Nullable reference types are strictly enabled (`<Nullable>enable</Nullable>`).
   - Treat warnings as errors is enabled (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`).
4. **Architectural Analyzers**:
   - All code must satisfy rules `ELMSG002`, `ELMSG004`, `ELMSG005`, `ELMSG008`, and `ELMSG010`.

---

## Branching & Commit Conventions

### Branch Strategy
- `main`: Production release branch. All release tags are cut from `main`.
- `develop`: Integration and feature development branch.
- Feature branches: `feat/<feature-name>`, `fix/<issue-name>`, `docs/<topic>`.

### Conventional Commits
This repository uses [Release Please](https://github.com/googleapis/release-please) for automated SemVer releases and changelog generation based on [Conventional Commits](https://www.conventionalcommits.org/):

| Prefix | Description | SemVer Impact |
| :--- | :--- | :--- |
| `feat:` | New feature or capability | Minor release (`0.X.0` or `X.Y.0`) |
| `fix:` | Bug fix or defect correction | Patch release (`0.0.X` or `X.Y.Z`) |
| `perf:` | Performance optimization | Patch release |
| `breaking:` / `feat!:` | Breaking API or contract change | Major release (`X.0.0`) |
| `docs:` | Documentation update | None (chore) |
| `test:` | Test suite addition or enhancement | None (chore) |
| `chore:` / `ci:` / `build:` | Maintenance, CI/CD, or build changes | None |

Example:
```text
feat(transport): add batch publishing support for Azure Service Bus
fix(analyzers): prevent false positive on generic scoped handler registration
docs(architecture): add message upcasting sequence diagram
```

---

## Pull Request Checklist

Before submitting a PR, verify:

- [ ] Code builds cleanly with `dotnet build EricksonLopez.Messaging.slnx --configuration Release -p:TreatWarningsAsErrors=true`.
- [ ] All automated tests pass with `dotnet test EricksonLopez.Messaging.slnx --configuration Release`.
- [ ] `dotnet pack` generates all 11 `.nupkg` packages without warnings or errors.
- [ ] New or modified code is covered by unit/integration tests (100% target).
- [ ] Stryker mutation testing satisfies the threshold gate ($\ge 95\%$).
- [ ] Roslyn architectural rules (`ELMSG002`, `ELMSG004`, `ELMSG005`, `ELMSG008`, `ELMSG010`) are respected.
- [ ] Commit messages follow the Conventional Commits format.

---

## Code of Conduct

All contributors are expected to adhere to our [Code of Conduct](CODE_OF_CONDUCT.md).