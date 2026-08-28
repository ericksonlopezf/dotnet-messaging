// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Events.Contracts;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Events;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Messaging.Events.Tests;

[Trait("Category", "Unit")]
public sealed class MessagingEventsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMessagingEventPublisher_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection? services = null;
        Action act = () => services!.AddMessagingEventPublisher();
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services")
            .WithMessage("*Service collection cannot be null.*");
    }

    [Fact]
    public void AddMessagingEventPublisher_WithoutConfigure_RegistersSingletonOptionsAndScopedPublisher()
    {
        var services = new ServiceCollection();
        var publisher = Substitute.For<IMessagePublisher>();
        services.AddSingleton(publisher);

        services.AddMessagingEventPublisher();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetService<MessagingEventsOptions>();
        options.Should().NotBeNull();
        options!.ThrowOnFailure.Should().BeTrue();

        var eventPublisher = provider.GetService<IEventPublisher>();
        eventPublisher.Should().NotBeNull();
        eventPublisher.Should().BeOfType<MessagingEventPublisher>();
    }

    [Fact]
    public void AddMessagingEventPublisher_WithConfigure_AppliesOptionsAndRegistersSingleton()
    {
        var services = new ServiceCollection();
        var publisher = Substitute.For<IMessagePublisher>();
        services.AddSingleton(publisher);

        services.AddMessagingEventPublisher(cfg =>
        {
            cfg.ThrowOnFailure = false;
            cfg.DestinationResolver = t => "my.custom.destination";
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<MessagingEventsOptions>();
        options.ThrowOnFailure.Should().BeFalse();
        options.DestinationResolver.Should().NotBeNull();
        options.DestinationResolver!(typeof(int)).Should().Be("my.custom.destination");

        var eventPublisher = provider.GetRequiredService<IEventPublisher>();
        eventPublisher.Should().NotBeNull();
        eventPublisher.Should().BeOfType<MessagingEventPublisher>();
    }
}
