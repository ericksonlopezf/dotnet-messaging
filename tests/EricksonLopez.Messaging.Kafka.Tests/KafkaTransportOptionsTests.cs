// Copyright © Erickson Lopez. MIT License.
using AwesomeAssertions;
using EricksonLopez.Messaging.Transport.Kafka;
using Xunit;

namespace EricksonLopez.Messaging.Kafka.Tests;

[Trait("Category", "Unit")]
public class KafkaTransportOptionsTests
{
    [Fact]
    public void KafkaTransportOptions_DefaultValues_AreCorrect()
    {
        var options = new KafkaTransportOptions();

        options.BootstrapServers.Should().Be("localhost:9092");
        options.GroupId.Should().Be("ericksonlopez-messaging-group");
        options.ClientId.Should().BeNull();
        options.EnableAutoCommit.Should().BeFalse();
    }

    [Fact]
    public void KafkaTransportOptions_CustomValues_CanBeAssigned()
    {
        var options = new KafkaTransportOptions
        {
            BootstrapServers = "kafka.internal:9094",
            GroupId = "order-consumers",
            ClientId = "order-publisher",
            EnableAutoCommit = true
        };

        options.BootstrapServers.Should().Be("kafka.internal:9094");
        options.GroupId.Should().Be("order-consumers");
        options.ClientId.Should().Be("order-publisher");
        options.EnableAutoCommit.Should().BeTrue();
    }
}
