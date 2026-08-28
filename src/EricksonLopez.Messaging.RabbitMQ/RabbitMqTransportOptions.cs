// Copyright © Erickson Lopez. MIT License.
namespace EricksonLopez.Messaging.Transport.RabbitMQ;

/// <summary>
/// Specifies configuration options for the RabbitMQ transport.
/// </summary>
public sealed class RabbitMqTransportOptions
{
    /// <summary>
    /// Gets or sets the RabbitMQ server host name.
    /// </summary>
    public string HostName { get; set; } = "localhost";

    /// <summary>
    /// Gets or sets the RabbitMQ port number.
    /// </summary>
    public int Port { get; set; } = 5672;

    /// <summary>
    /// Gets or sets the RabbitMQ virtual host.
    /// </summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>
    /// Gets or sets the AMQP username for broker authentication.
    /// </summary>
    public string UserName { get; set; } = "guest";

    /// <summary>
    /// Gets or sets the AMQP password for broker authentication.
    /// </summary>
    public string Password { get; set; } = "guest";

    /// <summary>
    /// Gets or sets the default AMQP exchange name for outgoing publish operations.
    /// </summary>
    /// <remarks>An empty string routes messages through the AMQP default exchange, delivering directly to a queue whose name matches the routing key.</remarks>
    public string ExchangeName { get; set; } = string.Empty;
}

