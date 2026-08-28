// Copyright © Erickson Lopez. MIT License.
using System;
using Amazon.SQS;
using AwesomeAssertions;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Messaging.Transport.AwsSqs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Messaging.AwsSqs.Tests;

[Trait("Category", "Unit")]
public class AwsSqsMessagingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAwsSqsMessagingTransport_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection? services = null;
        Action act = () => services!.AddAwsSqsMessagingTransport();
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services")
            .WithMessage("*Service collection cannot be null.*");
    }

    [Fact]
    public void AddAwsSqsMessagingTransport_WithoutConfigure_RegistersSingletonTransport()
    {
        var services = new ServiceCollection();
        var client = Substitute.For<IAmazonSQS>();
        services.AddSingleton(client);
        services.AddAwsSqsMessagingTransport();

        using var provider = services.BuildServiceProvider();
        var transport = provider.GetService<IMessageTransport>();
        transport.Should().NotBeNull();
        transport.Should().BeOfType<AwsSqsMessageTransport>();
    }

    [Fact]
    public void AddAwsSqsMessagingTransport_WithConfigure_RegistersOptionsAndSingleton()
    {
        var services = new ServiceCollection();
        var client = Substitute.For<IAmazonSQS>();
        services.AddSingleton(client);
        services.AddAwsSqsMessagingTransport(cfg =>
        {
            cfg.Region = "sa-east-1";
            cfg.WaitTimeSeconds = 15;
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AwsSqsTransportOptions>>().Value;
        options.Region.Should().Be("sa-east-1");
        options.WaitTimeSeconds.Should().Be(15);

        var transport = provider.GetService<IMessageTransport>();
        transport.Should().NotBeNull();
        transport.Should().BeOfType<AwsSqsMessageTransport>();
    }
}
