// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.Contracts;

/// <summary>
/// Represents an item in a batch message dispatch operation.
/// </summary>
/// <param name="MessageType">The message type identifier.</param>
/// <param name="Payload">The raw message payload bytes.</param>
/// <param name="Metadata">The accompanying message metadata.</param>
public readonly record struct MessageDispatchItem(
    string MessageType,
    ReadOnlyMemory<byte> Payload,
    TransportMessageMetadata Metadata);
