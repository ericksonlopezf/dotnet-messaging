# ADR-015: Institutionalization of Osherove Test Naming Pattern and IDE1006 Local Suppression

## Context
Standard .NET code analysis rules enforce PascalCase naming conventions on all class methods, raising warnings `IDE1006` (Naming rule violation) and `CA1707` (Identifiers should not contain underscores) when underscores are present.

However, unit and integration test methods serve as **executable living specifications** and living documentation. In test execution reports, continuous integration logs (Azure Pipelines, GitHub Actions, GitLab CI), and IDE test runners, test identifiers must be instantly readable and express:
1. The **Unit of Work / Subject Under Test** (Method or component being tested).
2. The **State Under Test / Scenario** (Preconditions, inputs, or edge conditions).
3. The **Expected Behavior / Result** (Observable outcome, returned value, or exception).

The Roy Osherove naming convention (`[UnitOfWork]_[Scenario]_[ExpectedBehavior]`) provides the industry-standard syntax for self-descriptive tests in .NET.

## Decision
- The **Roy Osherove naming pattern** (`Method_Scenario_ExpectedResult` / `UnitOfWork_StateUnderTest_ExpectedBehavior`) is institutionalized across all test projects in the `EricksonLopez.Messaging` ecosystem.
- Diagnostic rules `IDE1006` and `CA1707` are **explicitly suppressed locally in all test projects** via `Directory.Build.props` and project files.
- Production code in `src/` continues to enforce strict PascalCase conventions without underscores.

## Consequences
- Test failures in CI logs and command-line outputs are instantly understandable without navigating to source code.
- Developers maintain uniform, readable test method names across the entire test suite.
- Eliminates noise and false positive warnings in test builds while maintaining zero warnings as errors (`TreatWarningsAsErrors=true`).
