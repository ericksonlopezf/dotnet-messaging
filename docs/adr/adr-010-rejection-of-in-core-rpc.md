# ADR-010: Rejection of Request/Response RPC in Messaging Core

## Context
Implementing synchronous Request/Response (RPC) over message queues introduces artificial temporal coupling, high connection/queue overhead (temporary response queues), and encourages anti-patterns in distributed microservice architectures.

## Decision
- Synchronous Request/Response RPC over queues is explicitly **REJECTED** from the `EricksonLopez.Messaging` core.
- Inter-service synchronous queries should use **gRPC / HTTP REST**.
- Long-running asynchronous coordination should use **Event Choreography** or **Saga Orchestration (`EricksonLopez.Processes`)**.

## Consequences
- Prevents anti-patterns in distributed systems.
- Keeps core messaging API minimal and unambiguous.
