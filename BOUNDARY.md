# Architectural Boundary Specification: EricksonLopez.Messaging.Abstractions

## 1. Purpose
`EricksonLopez.Messaging.Abstractions` defines pure, transport-agnostic, low-allocation contracts for distributed message publishing, message consumption, consumer middleware pipelines, serialization contracts, and envelope metadata.

## 2. Owns
- Contracts: `IMessage`, `IMessageHandler<TMessage>`, `IMessagePublisher`, `IMessageConsumer`, `IMessageSerializer`, `IMessageUpcaster`, `IDeadLetterQueue`.
- Metadata & Context: `TransportMessageMetadata`, `MessageContext`, `MessageEnvelope<T>`, `MessagePublishOptions`, `MessageSendOptions`.
- Attributes: `[MessageType]`, `[PartitionKey]`.

## 3. Does Not Own
- Background hosted consumer services (`EricksonLopez.Messaging`).
- Concrete broker transport drivers (`EricksonLopez.Messaging.RabbitMQ`, `Kafka`, `AwsSqs`, `AzureServiceBus`).
- Domain event bus orchestration (`EricksonLopez.Events.Contracts` / `EricksonLopez.Events`).
- Transactional outbox persistence (`EricksonLopez.Outbox`).
- In-process CQRS mediation (`EricksonLopez.Mediator`).

## 4. Allowed Dependencies
- **.NET BCL only** (`net10.0`).
- Peer foundational package `EricksonLopez.Result` (formalized via [ADR-008](docs/adr/adr-008-result-pattern-integration.md) for functional error railway control).

## 5. Forbidden Dependencies
- Broker SDKs (`RabbitMQ.Client`, `Confluent.Kafka`, `AWSSDK.SQS`, `Azure.Messaging.ServiceBus`).
- In-process domain event contracts (`EricksonLopez.Events.Contracts` per [ADR-002](docs/adr/adr-002-messaging-vs-eventbus.md)).
- Persistence or ORM frameworks (EF Core, Dapper).

## 6. Who Can Depend On It
- `EricksonLopez.Messaging` (Core engine).
- `EricksonLopez.Messaging.*` (Broker transport adapters).
- `EricksonLopez.Messaging.Events` (Event bridge package).
- Application and infrastructure projects defining message contracts and handlers.

## 7. Public API Rules
- Zero serialization assumption at the contract layer; payload abstractions support binary `ReadOnlyMemory<byte>` and strongly-typed generics.
- Handlers must return `ValueTask<Result>` for functional error propagation.

## 8. Native AOT & Trimming Expectations
- `<IsAotCompatible>true</IsAotCompatible>`.
- `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>`.
- Zero reflection on dispatch hot paths.

## 9. Provider Isolation
- 100% broker-agnostic. All provider-specific options live inside dedicated transport adapter packages.

## 10. Testing Isolation
- `InMemoryTestHarness`, `PublishedMessageList`, and `ConsumedMessageList` live in `EricksonLopez.Messaging.Testing`.
