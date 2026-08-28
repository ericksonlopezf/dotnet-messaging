// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;

namespace EricksonLopez.Messaging.Events;

/// <summary>
/// Specifies configuration options for the messaging event publisher bridge.
/// </summary>
public sealed class MessagingEventsOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether an exception is thrown when publishing fails.
    /// </summary>
    public bool ThrowOnFailure { get; set; } = true;

    /// <summary>
    /// Gets or sets a custom function to resolve destination topic or exchange names for a given event type.
    /// </summary>
    public Func<Type, string?>? DestinationResolver { get; set; }
}
