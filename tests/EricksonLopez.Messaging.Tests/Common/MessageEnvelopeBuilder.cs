// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;

namespace EricksonLopez.Messaging.Tests.Common;

using EricksonLopez.Messaging.Contracts;

/// <summary>
/// Fluent test builder for constructing configured <see cref="MessageEnvelope{T}"/> instances.
/// </summary>
public sealed class MessageEnvelopeBuilder<T> where T : class, IMessage
{
    private T? _payload;
    private string _messageType = "test.event.v1";
    private string? _correlationId = "corr-test-builder";
    private string? _causationId;
    private string? _traceParent;
    private string? _tenantId;
    private string? _partitionKey;
    private string _contentType = "application/json";
    private int _schemaVersion = 1;
    private readonly Dictionary<string, string> _headers = new();

    /// <summary>
    /// Sets the payload instance.
    /// </summary>
    public MessageEnvelopeBuilder<T> WithPayload(T payload)
    {
        _payload = payload ?? throw new ArgumentNullException(nameof(payload));
        return this;
    }

    /// <summary>
    /// Sets the message type identifier.
    /// </summary>
    public MessageEnvelopeBuilder<T> WithMessageType(string messageType)
    {
        _messageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
        return this;
    }

    /// <summary>
    /// Sets the correlation identifier.
    /// </summary>
    public MessageEnvelopeBuilder<T> WithCorrelationId(string? correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    /// <summary>
    /// Sets the causation identifier.
    /// </summary>
    public MessageEnvelopeBuilder<T> WithCausationId(string? causationId)
    {
        _causationId = causationId;
        return this;
    }

    /// <summary>
    /// Sets the W3C traceparent context.
    /// </summary>
    public MessageEnvelopeBuilder<T> WithTraceParent(string? traceParent)
    {
        _traceParent = traceParent;
        return this;
    }

    /// <summary>
    /// Sets the multi-tenant identifier.
    /// </summary>
    public MessageEnvelopeBuilder<T> WithTenantId(string? tenantId)
    {
        _tenantId = tenantId;
        return this;
    }

    /// <summary>
    /// Sets the partition key.
    /// </summary>
    public MessageEnvelopeBuilder<T> WithPartitionKey(string? partitionKey)
    {
        _partitionKey = partitionKey;
        return this;
    }

    /// <summary>
    /// Sets the content type.
    /// </summary>
    public MessageEnvelopeBuilder<T> WithContentType(string contentType)
    {
        _contentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
        return this;
    }

    /// <summary>
    /// Sets the schema version.
    /// </summary>
    public MessageEnvelopeBuilder<T> WithSchemaVersion(int schemaVersion)
    {
        _schemaVersion = schemaVersion;
        return this;
    }

    /// <summary>
    /// Appends a custom header key-value pair.
    /// </summary>
    public MessageEnvelopeBuilder<T> WithHeader(string key, string value)
    {
        _headers[key] = value;
        return this;
    }

    /// <summary>
    /// Appends multiple custom headers.
    /// </summary>
    public MessageEnvelopeBuilder<T> WithHeaders(IDictionary<string, string> headers)
    {
        if (headers != null)
        {
            foreach (var kvp in headers)
            {
                _headers[kvp.Key] = kvp.Value;
            }
        }
        return this;
    }

    /// <summary>
    /// Builds the configured <see cref="MessageEnvelope{T}"/> instance.
    /// </summary>
    public MessageEnvelope<T> Build()
    {
        if (_payload == null)
        {
            throw new InvalidOperationException("Payload must be specified before building MessageEnvelope.");
        }

        var metadata = new TransportMessageMetadata(
            MessageId: Guid.NewGuid().ToString("N"),
            MessageType: _messageType,
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: _correlationId ?? Guid.NewGuid().ToString("N"),
            CausationId: _causationId,
            TraceParent: _traceParent,
            TenantId: _tenantId,
            PartitionKey: _partitionKey,
            ContentType: _contentType,
            SchemaVersion: _schemaVersion,
            Headers: _headers.Count > 0 ? new Dictionary<string, string>(_headers) : null);

        return new MessageEnvelope<T>(_payload, metadata);
    }
}



