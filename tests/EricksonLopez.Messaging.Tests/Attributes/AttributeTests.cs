// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.Tests.Attributes;

using System.Reflection;
using AwesomeAssertions;
using EricksonLopez.Messaging.Attributes;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

[Trait("Category", "Unit")]
public class AttributeTests
{
    [Fact]
    public void MessageTypeAttribute_ValidTypeName_SetsProperty()
    {
        // Arrange & Act
        var attr = new MessageTypeAttribute("orders.created.v1");

        // Assert
        attr.TypeName.Should().Be("orders.created.v1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void MessageTypeAttribute_NullOrWhiteSpace_ThrowsArgumentException(string? invalidName)
    {
        // Act
        Action act = () => _ = new MessageTypeAttribute(invalidName!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Property]
    public bool MessageTypeAttribute_AnyNonWhiteSpaceString_InstantiatesCorrectly(NonNull<string> name)
    {
        if (string.IsNullOrWhiteSpace(name.Get))
        {
            return true;
        }

        var attr = new MessageTypeAttribute(name.Get);
        return attr.TypeName == name.Get;
    }

    [Fact]
    public void MessageTypeAttribute_AttributeUsage_HasCorrectConstraints()
    {
        // Arrange
        var usage = typeof(MessageTypeAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        // Assert
        usage.Should().NotBeNull();
        usage!.ValidOn.Should().Be(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface);
        usage.Inherited.Should().BeFalse();
        usage.AllowMultiple.Should().BeFalse();
    }

    [Fact]
    public void PartitionKeyAttribute_DefaultConstructor_InstantiatesSuccessfully()
    {
        // Arrange & Act
        var attr = new PartitionKeyAttribute();

        // Assert
        attr.Should().NotBeNull();
    }

    [Fact]
    public void PartitionKeyAttribute_AttributeUsage_HasCorrectConstraints()
    {
        // Arrange
        var usage = typeof(PartitionKeyAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        // Assert
        usage.Should().NotBeNull();
        usage!.ValidOn.Should().Be(AttributeTargets.Property);
        usage.Inherited.Should().BeTrue();
        usage.AllowMultiple.Should().BeFalse();
    }
}


