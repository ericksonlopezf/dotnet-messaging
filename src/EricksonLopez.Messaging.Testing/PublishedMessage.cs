// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.Testing;

using EricksonLopez.Messaging.Contracts;

/// <summary>
/// Represents a message published through <see cref="InMemoryTestHarness"/> during test execution.
/// </summary>
/// <param name="Destination">The target queue, topic, or exchange name.</param>
/// <param name="Payload">The raw serialized payload bytes.</param>
/// <param name="Metadata">The message metadata headers.</param>
public sealed record PublishedMessage(
    string Destination,
    ReadOnlyMemory<byte> Payload,
    TransportMessageMetadata Metadata);
