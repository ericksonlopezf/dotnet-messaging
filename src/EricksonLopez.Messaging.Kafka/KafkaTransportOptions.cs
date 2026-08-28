// Copyright © Erickson Lopez. MIT License.
namespace EricksonLopez.Messaging.Transport.Kafka;

/// <summary>
/// Specifies configuration options for the Apache Kafka transport.
/// </summary>
public sealed class KafkaTransportOptions
{
    /// <summary>
    /// Gets or sets the comma-delimited list of initial broker host and port addresses.
    /// </summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>
    /// Gets or sets the consumer group identifier.
    /// </summary>
    public string GroupId { get; set; } = "ericksonlopez-messaging-group";

    /// <summary>
    /// Gets or sets the client identifier used for broker logging and metrics.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Kafka offset commits are performed automatically after each message is received.
    /// </summary>
    /// <remarks>When <see langword="false"/>, offsets are committed manually only after the handler returns <see cref="EricksonLopez.Messaging.Transport.TransportAckResult.Ack"/>.</remarks>
    public bool EnableAutoCommit { get; set; }
}
