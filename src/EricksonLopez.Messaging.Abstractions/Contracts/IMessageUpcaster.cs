// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.Contracts;

/// <summary>
/// Defines a transformation mechanism for upgrading an older schema version of a message into a newer schema version.
/// </summary>
/// <typeparam name="TOldMessage">The previous message contract type.</typeparam>
/// <typeparam name="TNewMessage">The upgraded target message contract type.</typeparam>
public interface IMessageUpcaster<in TOldMessage, out TNewMessage>
    where TOldMessage : class, IMessage
    where TNewMessage : class, IMessage
{
    /// <summary>
    /// Converts an older message payload to the newer message contract.
    /// </summary>
    /// <param name="oldMessage">The incoming source message instance.</param>
    /// <param name="metadata">The message metadata associated with the incoming envelope.</param>
    /// <returns>The upgraded message instance.</returns>
    TNewMessage Upcast(TOldMessage oldMessage, TransportMessageMetadata metadata);
}
