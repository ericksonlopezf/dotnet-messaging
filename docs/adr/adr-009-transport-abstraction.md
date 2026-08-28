# ADR-009: Minimalist Byte-Oriented Transport Abstraction

## Context
Attempting to create a universal `IMessageBroker` abstraction that unifies Kafka topic-partition log offsets with RabbitMQ AMQP exchanges/queues and AWS SQS visibility timeouts creates a leaky, fragile abstraction.

## Decision
- `IMessageTransport` provides the minimal universal byte-in / byte-out interface: `PublishRawAsync` and `SubscribeAsync`.
- Broker-specific capabilities (e.g. partition keys, prefetch count, consumer groups) are configured via strongly-typed options per transport adapter.

## Consequences
- Clean, robust core abstraction.
- Zero feature compromises on specialized broker features.
