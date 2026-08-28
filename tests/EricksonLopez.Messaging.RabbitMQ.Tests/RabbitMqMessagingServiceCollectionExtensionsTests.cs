// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.RabbitMQ.Tests;

using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Messaging.Transport.RabbitMQ;
using global::RabbitMQ.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

[Trait("Category", "Unit")]
public class RabbitMqMessagingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRabbitMqMessagingTransport_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection? services = null;
        Action act = () => services!.AddRabbitMqMessagingTransport();
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services")
            .WithMessage("*Service collection cannot be null.*");
    }

    [Fact]
    public void AddRabbitMqMessagingTransport_WithoutConfigure_RegistersSingletonTransport()
    {
        var services = new ServiceCollection();
        var factory = Substitute.For<IConnectionFactory>();
        services.AddSingleton(factory);
        services.AddRabbitMqMessagingTransport();

        using var provider = services.BuildServiceProvider();
        var transport = provider.GetService<IMessageTransport>();
        transport.Should().NotBeNull();
        transport.Should().BeOfType<RabbitMqMessageTransport>();
    }

    [Fact]
    public void AddRabbitMqMessagingTransport_WithConfigure_RegistersOptionsAndSingleton()
    {
        var services = new ServiceCollection();
        services.AddRabbitMqMessagingTransport(cfg =>
        {
            cfg.HostName = "custom-rabbit";
            cfg.Port = 5672;
            cfg.ExchangeName = "custom-exchange";
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RabbitMqTransportOptions>>().Value;
        options.HostName.Should().Be("custom-rabbit");
        options.ExchangeName.Should().Be("custom-exchange");

        var transport = provider.GetService<IMessageTransport>();
        transport.Should().NotBeNull();
        transport.Should().BeOfType<RabbitMqMessageTransport>();
    }
}
