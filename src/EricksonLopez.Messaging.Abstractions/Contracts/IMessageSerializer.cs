// Copyright © Erickson Lopez. MIT License.
using System;
using System.Buffers;

namespace EricksonLopez.Messaging.Contracts;

/// <summary>
/// Defines serialization and deserialization operations for messaging payloads.
/// </summary>
public interface IMessageSerializer
{
    /// <summary>
    /// Gets the media content type identifier associated with this serializer (e.g., <c>application/json</c>).
    /// </summary>
    string ContentType { get; }

    /// <summary>
    /// Serializes a strongly-typed message payload into an immutable memory segment.
    /// </summary>
    /// <typeparam name="T">The message payload type.</typeparam>
    /// <param name="message">The message instance to serialize.</param>
    /// <returns>A <see cref="ReadOnlyMemory{T}"/> containing the serialized payload bytes.</returns>
    ReadOnlyMemory<byte> Serialize<T>(T message) where T : notnull;

    /// <summary>
    /// Serializes a strongly-typed message payload directly into a buffer writer.
    /// </summary>
    /// <typeparam name="T">The message payload type.</typeparam>
    /// <param name="message">The message instance to serialize.</param>
    /// <param name="writer">The target buffer writer receiving the encoded bytes.</param>
    void Serialize<T>(T message, IBufferWriter<byte> writer) where T : notnull;

    /// <summary>
    /// Deserializes binary payload data into a strongly-typed message instance.
    /// </summary>
    /// <typeparam name="T">The expected message type.</typeparam>
    /// <param name="bytes">The raw binary payload segment.</param>
    /// <returns>The deserialized message instance.</returns>
    T Deserialize<T>(ReadOnlyMemory<byte> bytes);

    /// <summary>
    /// Deserializes binary payload data into an object instance of the specified runtime type.
    /// </summary>
    /// <param name="bytes">The raw binary payload segment.</param>
    /// <param name="messageType">The expected target runtime type.</param>
    /// <returns>The deserialized message instance.</returns>
    object Deserialize(ReadOnlyMemory<byte> bytes, Type messageType);
}
