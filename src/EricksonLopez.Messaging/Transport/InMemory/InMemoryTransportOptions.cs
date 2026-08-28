// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;

namespace EricksonLopez.Messaging.Transport.InMemory;

using System.Threading.Channels;

/// <summary>
/// Specifies configuration options for the in-memory message transport.
/// </summary>
public sealed class InMemoryTransportOptions
{
    /// <summary>
    /// Gets or sets the capacity limit of the bounded in-memory channels.
    /// </summary>
    public int ChannelCapacity { get; set; } = 10000;

    /// <summary>
    /// Gets or sets the behavior when an in-memory channel reaches capacity.
    /// </summary>
    public BoundedChannelFullMode FullMode { get; set; } = BoundedChannelFullMode.Wait;
}


