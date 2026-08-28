// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Messaging.Tests.HealthChecks;

[Trait("Category", "Unit")]
public class MessagingHealthCheckTests
{
    [Fact]
    public void Constructor_Default_SetsNullPublisher()
    {
        // Act
        var healthCheck = new MessagingHealthCheck();

        // Assert
        healthCheck.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckHealthAsync_WhenPublisherIsNull_ReturnsDegradedResult()
    {
        // Arrange
        var healthCheck = new MessagingHealthCheck(publisher: null);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Be("Message publisher is not registered in the service provider.");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenPublisherIsRegistered_ReturnsHealthyResult()
    {
        // Arrange
        var publisher = Substitute.For<IMessagePublisher>();
        var healthCheck = new MessagingHealthCheck(publisher);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Messaging transport is active and ready to publish messages.");
    }

    [Fact]
    public void AddMessagingHealthCheck_WhenServicesNull_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act
        Action act = () => services!.AddMessagingHealthCheck();

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddMessagingHealthCheck_ValidServices_RegistersTransientHealthCheck()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var returnedServices = services.AddMessagingHealthCheck();

        // Assert
        returnedServices.Should().BeSameAs(services);
        using var provider = services.BuildServiceProvider();
        var healthCheck = provider.GetService<IHealthCheck>();
        healthCheck.Should().NotBeNull();
        healthCheck.Should().BeOfType<MessagingHealthCheck>();
    }
}
