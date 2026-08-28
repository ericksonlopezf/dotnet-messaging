// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using Confluent.Kafka;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Messaging.Transport.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Messaging.Kafka.Tests;

[Trait("Category", "Unit")]
public class KafkaMessagingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddKafkaMessagingTransport_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection? services = null;
        Action act = () => services!.AddKafkaMessagingTransport();
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services")
            .WithMessage("*Service collection cannot be null.*");
    }

    [Fact]
    public void AddKafkaMessagingTransport_WithoutConfigure_RegistersSingletonTransport()
    {
        var services = new ServiceCollection();
        var producer = Substitute.For<IProducer<string, byte[]>>();
        services.AddSingleton(producer);
        services.AddKafkaMessagingTransport();

        using var provider = services.BuildServiceProvider();
        var transport = provider.GetService<IMessageTransport>();
        transport.Should().NotBeNull();
        transport.Should().BeOfType<KafkaMessageTransport>();
    }

    [Fact]
    public void AddKafkaMessagingTransport_WithConfigure_RegistersOptionsAndSingleton()
    {
        var services = new ServiceCollection();
        var producer = Substitute.For<IProducer<string, byte[]>>();
        services.AddSingleton(producer);
        services.AddKafkaMessagingTransport(cfg =>
        {
            cfg.BootstrapServers = "kafka:9092";
            cfg.GroupId = "test-group";
            cfg.EnableAutoCommit = true;
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KafkaTransportOptions>>().Value;
        options.BootstrapServers.Should().Be("kafka:9092");
        options.GroupId.Should().Be("test-group");
        options.EnableAutoCommit.Should().BeTrue();

        var transport = provider.GetService<IMessageTransport>();
        transport.Should().NotBeNull();
        transport.Should().BeOfType<KafkaMessageTransport>();
    }
}
