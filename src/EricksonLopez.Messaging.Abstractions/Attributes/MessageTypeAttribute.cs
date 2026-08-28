// Copyright © Erickson Lopez. MIT License.
namespace EricksonLopez.Messaging.Attributes;

using System;

/// <summary>
/// Specifies a stable, cross-platform string identifier for a message type contract.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class MessageTypeAttribute : Attribute
{
    /// <summary>
    /// Gets the unique string identifier for this message type.
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageTypeAttribute"/> class with the specified type identifier.
    /// </summary>
    /// <param name="typeName">The unique, stable string identifier (e.g., 'orders.order-created.v1').</param>
    /// <exception cref="ArgumentException"><paramref name="typeName"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public MessageTypeAttribute(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        TypeName = typeName;
    }
}
