// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Xunit;

namespace EricksonLopez.Messaging.Tests.Diagnostics;

[Trait("Category", "Unit")]
public class MessagingMeterProviderBuilderExtensionsTests
{
    [Fact]
    public void AddMessagingInstrumentation_NullBuilder_ThrowsArgumentNullException()
    {
        // Arrange
        MeterProviderBuilder? builder = null;

        // Act
        Action act = () => builder!.AddMessagingInstrumentation();

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("builder");
    }

    [Fact]
    public void AddMessagingInstrumentation_ValidBuilder_ReturnsSameBuilderInstance()
    {
        // Arrange
        var builder = Sdk.CreateMeterProviderBuilder();

        // Act
        var result = builder.AddMessagingInstrumentation();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddMessagingInstrumentation_BuildsSuccessfully()
    {
        // Arrange & Act
        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMessagingInstrumentation()
            .Build();

        // Assert
        meterProvider.Should().NotBeNull();
    }
}
