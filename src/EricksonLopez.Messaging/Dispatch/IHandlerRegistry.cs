// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics.CodeAnalysis;

namespace EricksonLopez.Messaging.Dispatch;

using EricksonLopez.Messaging.Contracts;

/// <summary>
/// Defines an abstraction for registering message handler bindings with a dispatcher.
/// </summary>
public interface IHandlerRegistry
{
    /// <summary>
    /// Registers a handler binding for the specified message type identifier.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message payload to handle.</typeparam>
    /// <typeparam name="THandler">The type of the message handler.</typeparam>
    /// <param name="typeName">The unique message type identifier to associate with the handler.</param>
    void RegisterHandler<
        TMessage,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(string typeName)
        where TMessage : notnull
        where THandler : notnull, IMessageHandler<TMessage>;
}
