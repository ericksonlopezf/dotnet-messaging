// Copyright © Erickson Lopez. MIT License.
namespace EricksonLopez.Messaging.Attributes;

using System;

/// <summary>
/// Specifies that a property within a message contract serves as the partition key for message ordering and routing.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class PartitionKeyAttribute : Attribute
{
}
