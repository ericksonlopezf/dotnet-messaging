// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.Testing;

/// <summary>
/// Represents a message received and processed by a subscription handler during test execution.
/// </summary>
/// <param name="Destination">The destination queue, topic, or exchange name.</param>
/// <param name="Payload">The raw serialized payload bytes.</param>
/// <param name="Metadata">The message metadata headers.</param>
/// <param name="Succeeded">A value indicating whether the handler acknowledged the message successfully.</param>
public sealed record ConsumedMessage(
    string Destination,
    ReadOnlyMemory<byte> Payload,
    EricksonLopez.Messaging.Contracts.TransportMessageMetadata Metadata,
    bool Succeeded);
