// Copyright © Erickson Lopez. MIT License.
namespace EricksonLopez.Messaging.Generators.Models;

using System;

internal readonly struct PartitionKeyInfo : IEquatable<PartitionKeyInfo>
{
    public string MessageFullName { get; }
    public string PropertyName { get; }
    public string PropertyType { get; }

    public PartitionKeyInfo(
        string messageFullName,
        string propertyName,
        string propertyType)
    {
        MessageFullName = messageFullName;
        PropertyName = propertyName;
        PropertyType = propertyType;
    }

    public bool Equals(PartitionKeyInfo other)
    {
        return string.Equals(MessageFullName, other.MessageFullName, StringComparison.Ordinal) &&
               string.Equals(PropertyName, other.PropertyName, StringComparison.Ordinal) &&
               string.Equals(PropertyType, other.PropertyType, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) => obj is PartitionKeyInfo other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (MessageFullName != null ? StringComparer.Ordinal.GetHashCode(MessageFullName) : 0);
            hash = (hash * 397) ^ (PropertyName != null ? StringComparer.Ordinal.GetHashCode(PropertyName) : 0);
            hash = (hash * 397) ^ (PropertyType != null ? StringComparer.Ordinal.GetHashCode(PropertyType) : 0);
            return hash;
        }
    }

    public static bool operator ==(PartitionKeyInfo left, PartitionKeyInfo right) => left.Equals(right);
    public static bool operator !=(PartitionKeyInfo left, PartitionKeyInfo right) => !left.Equals(right);
}
