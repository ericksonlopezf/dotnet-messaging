// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.Dispatch;

/// <summary>
/// Defines a deferred handler registration binding executed during dispatcher initialization.
/// </summary>
public interface IHandlerRegistration
{
    /// <summary>
    /// Gets the unique string message type identifier.
    /// </summary>
    string TypeName { get; }

    /// <summary>
    /// Registers the handler binding with the target message dispatcher.
    /// </summary>
    /// <param name="dispatcher">The dispatcher instance to configure.</param>
    void Register(DefaultMessageDispatcher dispatcher);

    /// <summary>
    /// Registers the handler binding with the target handler registry.
    /// </summary>
    /// <param name="registry">The handler registry to configure.</param>
    void Register(IHandlerRegistry registry)
    {
        if (registry is DefaultMessageDispatcher dispatcher)
        {
            Register(dispatcher);
        }
    }
}
