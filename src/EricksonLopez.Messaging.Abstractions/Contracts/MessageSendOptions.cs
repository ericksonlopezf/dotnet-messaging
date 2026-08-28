// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.Contracts;

using System.Collections.Generic;

/// <summary>
/// Specifies configuration options for sending point-to-point command messages.
/// </summary>
public sealed class MessageSendOptions
{
    /// <summary>
    /// Gets or sets the correlation identifier for end-to-end distributed tracing.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets the causation identifier of the triggering message or event.
    /// </summary>
    public string? CausationId { get; set; }

    /// <summary>
    /// Gets or sets the tenant identifier for multi-tenant isolation and routing.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets or sets the partition key used for deterministic message ordering across partitions.
    /// </summary>
    public string? PartitionKey { get; set; }

    /// <summary>
    /// Gets or sets custom key-value headers attached to the message envelope.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; set; }
}

