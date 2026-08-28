// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.RabbitMQ.Tests;

using AwesomeAssertions;
using EricksonLopez.Messaging.Transport.RabbitMQ;
using Xunit;

[Trait("Category", "Unit")]
public class RabbitMqTransportOptionsTests
{
    [Fact]
    public void RabbitMqTransportOptions_DefaultsAndSetters_WorkCorrectly()
    {
        // Arrange & Act
        var options = new RabbitMqTransportOptions();

        // Assert defaults
        options.HostName.Should().Be("localhost");
        options.Port.Should().Be(5672);
        options.VirtualHost.Should().Be("/");
        options.UserName.Should().Be("guest");
        options.Password.Should().Be("guest");
        options.ExchangeName.Should().BeEmpty();

        // Act - setters
        options.HostName = "rabbit.internal";
        options.Port = 5673;
        options.VirtualHost = "/app";
        options.UserName = "admin";
        options.Password = "secret";
        options.ExchangeName = "app.exchange";

        // Assert modified
        options.HostName.Should().Be("rabbit.internal");
        options.Port.Should().Be(5673);
        options.VirtualHost.Should().Be("/app");
        options.UserName.Should().Be("admin");
        options.Password.Should().Be("secret");
        options.ExchangeName.Should().Be("app.exchange");
    }
}
