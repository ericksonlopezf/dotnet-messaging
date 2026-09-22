// Copyright © Erickson Lopez. MIT License.
namespace EricksonLopez.Messaging.Generators.Models;

using System;

internal readonly struct HandlerDiscoveryInfo : IEquatable<HandlerDiscoveryInfo>
{
    public string HandlerFullName { get; }
    public string MessageFullName { get; }
    public string MessageTypeName { get; }
    public string HandlerName { get; }

    public HandlerDiscoveryInfo(
        string handlerFullName,
        string messageFullName,
        string messageTypeName,
        string handlerName)
    {
        HandlerFullName = handlerFullName;
        MessageFullName = messageFullName;
        MessageTypeName = messageTypeName;
        HandlerName = handlerName;
    }

    public bool Equals(HandlerDiscoveryInfo other)
    {
        return string.Equals(HandlerFullName, other.HandlerFullName, StringComparison.Ordinal) &&
               string.Equals(MessageFullName, other.MessageFullName, StringComparison.Ordinal) &&
               string.Equals(MessageTypeName, other.MessageTypeName, StringComparison.Ordinal) &&
               string.Equals(HandlerName, other.HandlerName, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) => obj is HandlerDiscoveryInfo other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (HandlerFullName != null ? StringComparer.Ordinal.GetHashCode(HandlerFullName) : 0);
            hash = (hash * 397) ^ (MessageFullName != null ? StringComparer.Ordinal.GetHashCode(MessageFullName) : 0);
            hash = (hash * 397) ^ (MessageTypeName != null ? StringComparer.Ordinal.GetHashCode(MessageTypeName) : 0);
            hash = (hash * 397) ^ (HandlerName != null ? StringComparer.Ordinal.GetHashCode(HandlerName) : 0);
            return hash;
        }
    }

    public static bool operator ==(HandlerDiscoveryInfo left, HandlerDiscoveryInfo right) => left.Equals(right);
    public static bool operator !=(HandlerDiscoveryInfo left, HandlerDiscoveryInfo right) => !left.Equals(right);
}
