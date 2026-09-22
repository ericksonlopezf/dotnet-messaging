// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.Middleware;

/// <summary>
/// Configures message deduplication behavior in the message processing pipeline.
/// </summary>
public sealed class MessageDeduplicationOptions
{
    /// <summary>
    /// Gets or sets the duration for which processed message identifiers are retained to prevent duplicate handling.
    /// The default is 24 hours.
    /// </summary>
    public TimeSpan Expiration { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets or sets a value indicating whether message deduplication is actively enforced.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
