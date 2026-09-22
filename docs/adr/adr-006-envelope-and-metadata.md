# ADR-006: MessageEnvelope and Metadata Strategy

## Status
Accepted

## Date
2026-09-04

## Context
Distributed messages need to transmit routing, tracking, and tenant information without polluting domain models.

## Decision
- Business payloads remain pure C# records without infrastructure properties.
- Transport wraps payloads in `MessageEnvelope<T>` containing `MessageMetadata`.
- `MessageMetadata` incorporates W3C `traceparent` for distributed tracing and `PartitionKey` for sharded brokers.

## Consequences
- Clean separation between business payload and transport headers.
- Full compliance with OpenTelemetry messaging semantic conventions.
