// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.Dispatch;

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

    /// <inheritdoc />
    public virtual void Register(IHandlerRegistry registry)
    {
        if (registry is DefaultMessageDispatcher dispatcher)
        {
            Register(dispatcher);
        }
    }
}
