# EricksonLopez.Messaging — Testing Guidelines & Architecture

This directory contains the automated test suite for `EricksonLopez.Messaging` and all satellite packages.

---

## Test Projects Structure

The test suite comprises 10 dedicated test projects verifying all 11 library packages:

```text
tests/
├── EricksonLopez.Messaging.Tests/               # Core contracts, dispatcher, pipeline, middlewares, serialization, in-memory transport
├── EricksonLopez.Messaging.Generators.Tests/   # Roslyn Incremental Generator unit & integration tests
├── EricksonLopez.Messaging.Analyzers.Tests/    # Roslyn Architectural Analyzer rule tests (ELMSG002, ELMSG004, ELMSG005, ELMSG008, ELMSG010)
├── EricksonLopez.Messaging.AzureServiceBus.Tests/ # Azure Service Bus transport driver tests
├── EricksonLopez.Messaging.RabbitMQ.Tests/     # RabbitMQ transport driver tests
├── EricksonLopez.Messaging.AwsSqs.Tests/       # AWS SQS transport driver tests
├── EricksonLopez.Messaging.Kafka.Tests/        # Apache Kafka transport driver tests
├── EricksonLopez.Messaging.Events.Tests/       # Events bridge publisher tests
├── EricksonLopez.Messaging.Testing.Tests/      # In-memory test harness & assertion list tests
└── EricksonLopez.Messaging.AotSmokeTest/       # Native AOT smoke test: PublishAot=true executable validating trimming safety
```

> **Note**: `EricksonLopez.Messaging.AotSmokeTest` is not a standard xUnit test project (`<IsTestProject>false</IsTestProject>`). It is a `net10.0` console executable compiled with `<PublishAot>true</PublishAot>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` to validate that `EricksonLopez.Messaging.Abstractions`, `EricksonLopez.Messaging`, and `EricksonLopez.Messaging.Testing` compile without trimming warnings under Native AOT. Run with `dotnet publish -c Release` to verify AOT compatibility.

---

## Test Naming Standard (Roy Osherove Pattern)

In compliance with [ADR-015](../docs/adr/adr-015-test-naming-osherove-ide1006.md), all test methods across the ecosystem adhere to the **Roy Osherove pattern**:

$$\mathbf{[UnitOfWork]\_[StateUnderTest]\_[ExpectedBehavior]}$$

### Examples:
- `PublishAsync_PlainMessageWithoutAttribute_UsesTypeNameAndCustomDestination`
- `SubscribeAsync_WithOptionsFullModeDropOldest_DropsOldestWhenSaturated`
- `Constructor_NullSerializer_ThrowsArgumentNullException`
- `ConsumeAsync_WithCircuitBreakerOpen_FastFailsWithoutCallingHandler`

Diagnostic rules `IDE1006` and `CA1707` are suppressed in test projects to enable living specifications in CI logs and test runners.

---

## Test Categorization & Execution

Tests use xUnit `[Trait("Category", "...")]` for targeted execution:

| Trait Category | Scope | Description |
| :--- | :--- | :--- |
| `Unit` | Core / Middlewares / Serialization | In-memory unit tests with mock and stub isolation. |
| `Transport` | ASB / RabbitMQ / AWS / Kafka / InMemory | Transport drivers and network abstraction tests. |
| `Concurrency` | Consumer / Channels | High-concurrency throughput and thread-safety tests. |
| `Integration` | End-to-End Pipeline | Full publisher-dispatcher-consumer integration tests. |
| `Roslyn` | Generators & Analyzers | Compilation-based tests evaluating code generation and diagnostic rules. |

### Running Tests by Category

```bash
# Run all tests across the solution
dotnet test EricksonLopez.Messaging.slnx --configuration Release

# Run only unit tests
dotnet test --filter "Category=Unit"

# Run transport driver tests
dotnet test --filter "Category=Transport"

# Run concurrency tests
dotnet test --filter "Category=Concurrency"

# Run Roslyn analyzer/generator tests
dotnet test --filter "Category=Roslyn"

# Run integration tests
dotnet test --filter "Category=Integration"
```

---

## Mutation Testing (Stryker.NET)

The entire codebase is evaluated under Stryker.NET mutation testing. Thresholds are defined consistently across all `stryker-*.json` files:

- **High (Green)**: $\ge 100\%$
- **Low (Yellow)**: $\ge 98\%$
- **Warning (Orange)**: $\ge 95\%$
- **Break (Failure)**: $< 95\%$

Non-observable plumbing methods (`Log*`, `ConfigureAwait`, `Dispose`, and `ToString`) are ignored from mutation generation to prevent artificial coupling to diagnostic internals.

```bash
# Run Stryker across Core
dotnet stryker --config-file stryker-config.json
```

---

## Test Infrastructure & Helpers

- **`TestMessageContextFactory`**: Factory for creating immutable `MessageContext` and `TransportMessageMetadata` instances for testing.
- **`MessageEnvelopeBuilder<T>`**: Fluent test builder for creating immutable `MessageEnvelope<T>` instances with customized headers and metadata.
- **`AzureServiceBusTestFactory`**: Helper for instantiating `ProcessMessageEventArgs` for `Azure.Messaging.ServiceBus` testing.
- **Property-Based Testing**: `FsCheck.Xunit` is integrated for contract fuzzing, randomized payload validation, and JSON round-trip testing.
