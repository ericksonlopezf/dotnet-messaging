// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Messaging.Generators.Models;
using Xunit;

namespace EricksonLopez.Messaging.Generators.Tests;

[Trait("Category", "Roslyn")]
public class ModelsTests
{
    [Fact]
    public void HandlerDiscoveryInfo_PropertiesAndEquality_WorkAsExpected()
    {
        var info1 = new HandlerDiscoveryInfo(
            handlerFullName: "Sample.MyHandler",
            messageFullName: "Sample.MyMessage",
            messageTypeName: "my-msg",
            handlerName: "MyHandler");

        var info2 = new HandlerDiscoveryInfo(
            handlerFullName: "Sample.MyHandler",
            messageFullName: "Sample.MyMessage",
            messageTypeName: "my-msg",
            handlerName: "MyHandler");

        info1.HandlerFullName.Should().Be("Sample.MyHandler");
        info1.MessageFullName.Should().Be("Sample.MyMessage");
        info1.MessageTypeName.Should().Be("my-msg");
        info1.HandlerName.Should().Be("MyHandler");

        // Equals
        info1.Equals(info2).Should().BeTrue();
        (info1 == info2).Should().BeTrue();
        (info1 != info2).Should().BeFalse();
        info1.Equals((object)info2).Should().BeTrue();
        info1.Equals((object?)null).Should().BeFalse();
        info1.Equals("not-a-struct").Should().BeFalse();
        info1.GetHashCode().Should().Be(info2.GetHashCode());

        // Field differences
        var diffHandlerFull = new HandlerDiscoveryInfo("Other.MyHandler", "Sample.MyMessage", "my-msg", "MyHandler");
        info1.Equals(diffHandlerFull).Should().BeFalse();
        (info1 == diffHandlerFull).Should().BeFalse();
        (info1 != diffHandlerFull).Should().BeTrue();
        info1.GetHashCode().Should().NotBe(diffHandlerFull.GetHashCode());

        var diffMsgFull = new HandlerDiscoveryInfo("Sample.MyHandler", "Other.MyMessage", "my-msg", "MyHandler");
        info1.Equals(diffMsgFull).Should().BeFalse();
        info1.GetHashCode().Should().NotBe(diffMsgFull.GetHashCode());

        var diffMsgType = new HandlerDiscoveryInfo("Sample.MyHandler", "Sample.MyMessage", "other-msg", "MyHandler");
        info1.Equals(diffMsgType).Should().BeFalse();
        info1.GetHashCode().Should().NotBe(diffMsgType.GetHashCode());

        var diffHandlerName = new HandlerDiscoveryInfo("Sample.MyHandler", "Sample.MyMessage", "my-msg", "OtherHandler");
        info1.Equals(diffHandlerName).Should().BeFalse();
        info1.GetHashCode().Should().NotBe(diffHandlerName.GetHashCode());

        // Null fields in constructor
        var nullFields = new HandlerDiscoveryInfo(null!, null!, null!, null!);
        nullFields.GetHashCode().Should().Be(0);
        nullFields.Equals(info1).Should().BeFalse();
    }

    [Fact]
    public void PartitionKeyInfo_PropertiesAndEquality_WorkAsExpected()
    {
        var info1 = new PartitionKeyInfo(
            messageFullName: "Sample.OrderMessage",
            propertyName: "OrderId",
            propertyType: "System.Guid");

        var info2 = new PartitionKeyInfo(
            messageFullName: "Sample.OrderMessage",
            propertyName: "OrderId",
            propertyType: "System.Guid");

        info1.MessageFullName.Should().Be("Sample.OrderMessage");
        info1.PropertyName.Should().Be("OrderId");
        info1.PropertyType.Should().Be("System.Guid");

        // Equals
        info1.Equals(info2).Should().BeTrue();
        (info1 == info2).Should().BeTrue();
        (info1 != info2).Should().BeFalse();
        info1.Equals((object)info2).Should().BeTrue();
        info1.Equals((object?)null).Should().BeFalse();
        info1.Equals(12345).Should().BeFalse();
        info1.GetHashCode().Should().Be(info2.GetHashCode());

        // Field differences
        var diffMsg = new PartitionKeyInfo("Other.OrderMessage", "OrderId", "System.Guid");
        info1.Equals(diffMsg).Should().BeFalse();
        (info1 == diffMsg).Should().BeFalse();
        (info1 != diffMsg).Should().BeTrue();
        info1.GetHashCode().Should().NotBe(diffMsg.GetHashCode());

        var diffProp = new PartitionKeyInfo("Sample.OrderMessage", "CustomerId", "System.Guid");
        info1.Equals(diffProp).Should().BeFalse();
        info1.GetHashCode().Should().NotBe(diffProp.GetHashCode());

        var diffType = new PartitionKeyInfo("Sample.OrderMessage", "OrderId", "System.Int64");
        info1.Equals(diffType).Should().BeFalse();
        info1.GetHashCode().Should().NotBe(diffType.GetHashCode());

        // Null fields
        var nullFields = new PartitionKeyInfo(null!, null!, null!);
        nullFields.GetHashCode().Should().Be(0);
        nullFields.Equals(info1).Should().BeFalse();
    }
}
