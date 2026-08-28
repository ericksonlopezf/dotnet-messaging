// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.Diagnostics;

using System.Diagnostics;
using System.Diagnostics.Metrics;

/// <summary>
/// Provides canonical OpenTelemetry <see cref="ActivitySource"/> and <see cref="Meter"/> instances for distributed messaging diagnostics.
/// </summary>
public static class MessagingDiagnostics
{
    /// <summary>
    /// Gets the canonical identifier name for OpenTelemetry activity tracing.
    /// </summary>
    public const string ActivitySourceName = "EricksonLopez.Messaging";

    /// <summary>
    /// Gets the canonical identifier name for OpenTelemetry metrics collection.
    /// </summary>
    public const string MeterName = "EricksonLopez.Messaging";

    /// <summary>
    /// Gets the messaging framework version tag.
    /// </summary>
    public const string Version = "1.0.0";

    /// <summary>
    /// Gets the shared <see cref="ActivitySource"/> used for distributed tracing across messaging operations.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    /// <summary>
    /// Gets the shared <see cref="Meter"/> used for publishing messaging operational metrics.
    /// </summary>
    public static readonly Meter Meter = new(MeterName, Version);

    /// <summary>
    /// Gets the counter instrument recording the total number of published messages.
    /// </summary>
    public static readonly Counter<long> MessagesPublished =
        Meter.CreateCounter<long>("messaging.publish.messages", "messages", "Total count of messages published.");

    /// <summary>
    /// Gets the counter instrument recording the total number of received messages.
    /// </summary>
    public static readonly Counter<long> MessagesReceived =
        Meter.CreateCounter<long>("messaging.receive.messages", "messages", "Total count of messages received.");

    /// <summary>
    /// Gets the counter instrument recording the total number of failed message processing attempts.
    /// </summary>
    public static readonly Counter<long> MessagesFailed =
        Meter.CreateCounter<long>("messaging.failed.messages", "messages", "Total count of failed message processing attempts.");

    /// <summary>
    /// Gets the histogram instrument tracking message processing latency in milliseconds.
    /// </summary>
    public static readonly Histogram<double> ProcessingDuration =
        Meter.CreateHistogram<double>("messaging.process.duration", "ms", "Processing duration of messages in milliseconds.");
}

