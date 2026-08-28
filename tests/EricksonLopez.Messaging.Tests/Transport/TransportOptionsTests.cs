// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;

namespace EricksonLopez.Messaging.Tests.Transport;

using System.Threading.Channels;
using AwesomeAssertions;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Messaging.Transport.InMemory;
using Xunit;

[Trait("Category", "Transport")]
public class TransportOptionsTests
{
    [Fact]
    public void InMemoryTransportOptions_DefaultsAndSetters_WorkCorrectly()
    {
        // Arrange & Act
        var options = new InMemoryTransportOptions();

        // Assert defaults
        options.ChannelCapacity.Should().Be(10000);
        options.FullMode.Should().Be(BoundedChannelFullMode.Wait);

        // Act - modify
        options.ChannelCapacity = 500;
        options.FullMode = BoundedChannelFullMode.DropOldest;

        // Assert modified
        options.ChannelCapacity.Should().Be(500);
        options.FullMode.Should().Be(BoundedChannelFullMode.DropOldest);
    }

    [Fact]
    public void TransportSubscriptionOptions_DefaultsAndSetters_WorkCorrectly()
    {
        // Arrange & Act
        var options = new TransportSubscriptionOptions();

        // Assert defaults
        options.MaxConcurrency.Should().Be(1);
        options.PrefetchCount.Should().Be(10);
        options.ConsumerGroup.Should().BeNull();

        // Act - modify
        options.MaxConcurrency = 8;
        options.PrefetchCount = 100;
        options.ConsumerGroup = "orders-consumer-group";

        // Assert modified
        options.MaxConcurrency.Should().Be(8);
        options.PrefetchCount.Should().Be(100);
        options.ConsumerGroup.Should().Be("orders-consumer-group");
    }

    [Fact]
    public void TransportAckResult_EnumValues_MatchContract()
    {
        // Assert
        ((int)TransportAckResult.Ack).Should().Be(0);
        ((int)TransportAckResult.NackRequeue).Should().Be(1);
        ((int)TransportAckResult.DeadLetter).Should().Be(2);
    }
}


