// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.Dispatch;

using System.Reflection;
using EricksonLopez.Messaging.Attributes;

/// <summary>
/// Provides cached resolution of message type name identifiers using static generic initialization.
/// </summary>
/// <remarks>
/// After initial generic type initialization, the resolved name is cached in a static field and accessed
/// with zero overhead on subsequent calls.
/// </remarks>
/// <typeparam name="TMessage">The message payload type.</typeparam>
internal static class MessageTypeCache<TMessage>
{
    /// <summary>
    /// Gets the canonical message type identifier.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2743:Static field in generic type", Justification = "By design: cached per distinct TMessage type.")]
    public static readonly string TypeName = ResolveTypeName();

    private static string ResolveTypeName()
    {
        var attr = typeof(TMessage).GetCustomAttribute<MessageTypeAttribute>();
        return attr?.TypeName ?? typeof(TMessage).Name;
    }
}


