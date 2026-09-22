# ADR-017: MessageMetadata Semantic Boundary vs Outbox Storage Metadata

## Status
Accepted — August 2026

## Date
2026-09-04

## Context
An ecosystem-wide architectural review identified that both `EricksonLopez.Messaging.Abstractions` and `EricksonLopez.Outbox.Abstractions` define a type named `MessageMetadata`.

The review evaluated whether these types represent accidental duplication or intentional specializations across different bounded contexts.

## Decision
Maintain both types independently, enforcing strict semantic boundaries:

1. **`EricksonLopez.Messaging.Abstractions.MessageMetadata` (Transport & Routing)**:
   - Defined as a strongly typed `sealed record`.
   - Represents network transport and routing concerns: `MessageId`, `MessageType`, `Timestamp`, `CorrelationId`, `CausationId`, `TraceParent`, `TenantId`, `PartitionKey`, `ContentType`, `SchemaVersion`, and transport `Headers`.
   - Purpose: Standardizes distributed message headers across physical brokers (RabbitMQ, Kafka, Azure Service Bus, AWS SQS).
   - Boundary: Must NOT be altered to absorb database persistence concerns.

2. **`EricksonLopez.Outbox.Abstractions.MessageMetadata` (Storage & Raw Persistence)**:
   - Defined as a high-performance `readonly struct` wrapping `ReadOnlyMemory<MetadataEntry>`.
   - Purpose: Zero-allocation container for storing and retrieving raw key-value headers in transactional outbox database tables.
   - Boundary: Must NOT model distributed routing or message broker concepts.

## Consequences
- **Positive**: Zero coupling between messaging transport contracts and relational/document database storage layouts.
- **Positive**: No artificial inheritance or forced shared abstractions between two distinct architectural tiers.
- **Maintenance**: XML documentation and architecture tests enforce that neither type crosses into the other's responsibility domain.
