// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics.CodeAnalysis;

namespace EricksonLopez.Messaging.Dispatch;

using EricksonLopez.Messaging.Contracts;

internal sealed class HandlerRegistration<TMessage, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler> : HandlerRegistrationBase
    where TMessage : notnull
    where THandler : class, IMessageHandler<TMessage>
{
    public HandlerRegistration(string typeName) : base(typeName)
    {
    }

    public override void Register(IHandlerRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.RegisterHandler<TMessage, THandler>(TypeName);
    }

    public override void Register(DefaultMessageDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        dispatcher.RegisterHandler<TMessage, THandler>(TypeName);
    }
}
