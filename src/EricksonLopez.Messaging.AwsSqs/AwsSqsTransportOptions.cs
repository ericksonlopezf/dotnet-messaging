// Copyright © Erickson Lopez. MIT License.
namespace EricksonLopez.Messaging.Transport.AwsSqs;

/// <summary>
/// Specifies configuration options for the AWS SQS message transport.
/// </summary>
public sealed class AwsSqsTransportOptions
{
    /// <summary>
    /// Gets or sets the AWS region system name (e.g., "us-east-1").
    /// </summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// Gets or sets the optional explicit service endpoint URL for local emulation or custom endpoints.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// Gets or sets the message poll wait time in seconds for long polling.
    /// </summary>
    public int WaitTimeSeconds { get; set; } = 20;

    /// <summary>
    /// Gets or sets the maximum number of messages to retrieve in a single batch.
    /// </summary>
    public int MaxNumberOfMessages { get; set; } = 10;
}
