// Copyright © Erickson Lopez. MIT License.
using System;
using Microsoft.Extensions.DependencyInjection;

namespace EricksonLopez.Messaging.Middleware;

using EricksonLopez.Messaging.Contracts;

/// <summary>
/// Adapts <see cref="IMessageUpcaster{TOldMessage, TNewMessage}"/> implementations to <see cref="IMessageUpcasterInvoker"/>.
/// </summary>
/// <typeparam name="TOld">The source message type.</typeparam>
/// <typeparam name="TNew">The target upgraded message type.</typeparam>
/// <typeparam name="TUpcaster">The type of upcaster implementation.</typeparam>
public sealed class MessageUpcasterInvoker<TOld, TNew, TUpcaster> : IMessageUpcasterInvoker
    where TOld : class, IMessage
    where TNew : class, IMessage
    where TUpcaster : class, IMessageUpcaster<TOld, TNew>
{
    /// <inheritdoc />
    public Type SourceType => typeof(TOld);

    /// <inheritdoc />
    public Type TargetType => typeof(TNew);

    /// <inheritdoc />
    public object Upcast(object message, TransportMessageMetadata metadata, IServiceProvider serviceProvider)
    {
        var upcaster = serviceProvider.GetRequiredService<TUpcaster>();
        return upcaster.Upcast((TOld)message, metadata);
    }
}
