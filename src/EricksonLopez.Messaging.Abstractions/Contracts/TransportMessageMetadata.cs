// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;

namespace EricksonLopez.Messaging.Contracts;

/// <summary>
/// Represents metadata associated with a message for distributed routing, tracing, and transport concerns.
/// </summary>
/// <param name="MessageId">The unique identifier of the message.</param>
/// <param name="MessageType">The stable string type identifier representing the payload schema.</param>
/// <param name="Timestamp">The UTC timestamp indicating when the message was generated.</param>
/// <param name="CorrelationId">The correlation identifier for end-to-end distributed tracing.</param>
/// <param name="CausationId">The causation identifier indicating the originating event or command, if available.</param>
/// <param name="TraceParent">The W3C traceparent string for distributed OpenTelemetry propagation, if available.</param>
/// <param name="TenantId">The tenant identifier for multi-tenant isolation and routing, if available.</param>
/// <param name="PartitionKey">The partition key for deterministic sharded ordering, if available.</param>
/// <param name="ContentType">The MIME content type of the serialized payload.</param>
/// <param name="SchemaVersion">The integer version of the payload schema contract.</param>
/// <param name="Headers">The custom key-value headers for transport extensions, if available.</param>
public record TransportMessageMetadata(
    string MessageId,
    string MessageType,
    DateTimeOffset Timestamp,
    string CorrelationId,
    string? CausationId = null,
    string? TraceParent = null,
    string? TenantId = null,
    string? PartitionKey = null,
    string ContentType = "application/json",
    int SchemaVersion = 1,
    IReadOnlyDictionary<string, string>? Headers = null)
{
    /// <summary>
    /// Creates a new <see cref="TransportMessageMetadata"/> instance with a generated message identifier, current UTC timestamp, and the specified optional fields.
    /// </summary>
    /// <param name="messageType">The stable string identifier for the message schema contract (e.g., <c>orders.order-created.v1</c>).</param>
    /// <param name="correlationId">The correlation identifier for distributed tracing. When <see langword="null"/>, a new identifier is generated automatically.</param>
    /// <param name="causationId">The causation identifier of the triggering message, if available.</param>
    /// <param name="traceParent">The W3C traceparent header value for OpenTelemetry propagation, if available.</param>
    /// <param name="tenantId">The identifier of the tenant context, if applicable.</param>
    /// <param name="partitionKey">The partition key for sharded message ordering, if applicable.</param>
    /// <param name="schemaVersion">The integer version of the payload schema contract. Defaults to <c>1</c>.</param>
    /// <param name="headers">The custom key-value transport extension headers, if any.</param>
    /// <returns>
    /// A new <see cref="TransportMessageMetadata"/> instance with a generated <see cref="MessageId"/>, the current UTC timestamp as <see cref="Timestamp"/>,
    /// and a generated <see cref="CorrelationId"/> when <paramref name="correlationId"/> is <see langword="null"/>.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="messageType"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public static TransportMessageMetadata Create(
        string messageType,
        string? correlationId = null,
        string? causationId = null,
        string? traceParent = null,
        string? tenantId = null,
        string? partitionKey = null,
        int schemaVersion = 1,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);

        return new TransportMessageMetadata(
            MessageId: Guid.NewGuid().ToString("N"),
            MessageType: messageType,
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: correlationId ?? Guid.NewGuid().ToString("N"),
            CausationId: causationId,
            TraceParent: traceParent,
            TenantId: tenantId,
            PartitionKey: partitionKey,
            ContentType: "application/json",
            SchemaVersion: schemaVersion,
            Headers: headers);
    }
}

