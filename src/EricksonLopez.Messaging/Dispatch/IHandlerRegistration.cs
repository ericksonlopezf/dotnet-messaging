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
}

/// <summary>
/// Provides an abstract base class for handler registrations with common type identifier storage.
/// </summary>
public abstract class HandlerRegistrationBase : IHandlerRegistration
{
    /// <inheritdoc />
    public string TypeName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HandlerRegistrationBase"/> class with the specified type identifier.
    /// </summary>
    /// <param name="typeName">The unique message type identifier.</param>
    protected HandlerRegistrationBase(string typeName)
    {
        TypeName = typeName;
    }

    /// <inheritdoc />
    public abstract void Register(DefaultMessageDispatcher dispatcher);
}


