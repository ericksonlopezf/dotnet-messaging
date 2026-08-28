// Copyright © Erickson Lopez. MIT License.
namespace EricksonLopez.Messaging.Contracts;

using System;

/// <summary>
/// Represents a strongly-typed carrier envelope bundling a message payload with its transport metadata.
/// </summary>
/// <typeparam name="TMessage">The type of the message payload.</typeparam>
/// <param name="Payload">The business payload instance.</param>
/// <param name="Metadata">The accompanying transport metadata.</param>
public sealed record MessageEnvelope<TMessage>(
    TMessage Payload,
    TransportMessageMetadata Metadata) where TMessage : notnull
{
    /// <summary>
    /// Creates a new <see cref="MessageEnvelope{TMessage}"/> instance with initialized metadata.
    /// </summary>
    /// <param name="payload">The business payload instance.</param>
    /// <param name="messageType">The message type identifier.</param>
    /// <param name="correlationId">The correlation identifier for distributed tracing.</param>
    /// <param name="causationId">The causation identifier of the triggering message.</param>
    /// <param name="traceParent">The W3C trace parent header value.</param>
    /// <param name="tenantId">The identifier of the tenant context.</param>
    /// <param name="partitionKey">The partition key for sharded messaging.</param>
    /// <param name="schemaVersion">The schema version number.</param>
    /// <returns>A new <see cref="MessageEnvelope{TMessage}"/> instance containing the payload and metadata.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="messageType"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public static MessageEnvelope<TMessage> Create(
        TMessage payload,
        string messageType,
        string? correlationId = null,
        string? causationId = null,
        string? traceParent = null,
        string? tenantId = null,
        string? partitionKey = null,
        int schemaVersion = 1)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var metadata = TransportMessageMetadata.Create(
            messageType: messageType,
            correlationId: correlationId,
            causationId: causationId,
            traceParent: traceParent,
            tenantId: tenantId,
            partitionKey: partitionKey,
            schemaVersion: schemaVersion);

        return new MessageEnvelope<TMessage>(payload, metadata);
    }
}
