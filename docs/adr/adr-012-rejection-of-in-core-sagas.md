# ADR-012: Rejection of In-Core Saga State Machine Orchestration

## Context
Complex multi-step saga state machines, saga correlation repositories, and compensation workflows require durable storage, state indexing, and timer scheduling that belong to process coordination rather than base messaging transport and routing.

## Decision
- State machine saga orchestration is **REJECTED** from `EricksonLopez.Messaging` core.
- Choreographed sagas use standard message publishing and event handling (`IMessageHandler<T>`).
- Orchestrated sagas are hosted in the specialized `EricksonLopez.Processes` package.

## Consequences
- Prevents monolithization and bloat of the core messaging library.
- Enables autonomous process manager evolution without impacting simple message producers/consumers.
