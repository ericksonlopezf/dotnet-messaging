// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using EricksonLopez.Result;

namespace EricksonLopez.Messaging.Tests.Diagnostics;

using AwesomeAssertions;
using EricksonLopez.Messaging.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

[Trait("Category", "Unit")]
public class MessagingTracerProviderBuilderExtensionsTests
{
    [Fact]
    public void AddMessagingInstrumentation_NullBuilder_ThrowsArgumentNullException()
    {
        // Arrange
        TracerProviderBuilder? builder = null;

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
        var builder = Sdk.CreateTracerProviderBuilder();

        // Act
        var result = builder.AddMessagingInstrumentation();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddMessagingInstrumentation_WhenConfigured_SubscribesToMessagingActivitySource()
    {
        var processor = new TestActivityProcessor();
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .SetSampler(new AlwaysOnSampler())
            .AddMessagingInstrumentation()
            .AddProcessor(processor)
            .Build();

        using (var activity = MessagingDiagnostics.ActivitySource.StartActivity("test.messaging.activity"))
        {
        }

        tracerProvider.ForceFlush();

        processor.Exported.Should().ContainSingle(a => a.OperationName == "test.messaging.activity");
    }

    private sealed class TestActivityProcessor : BaseProcessor<System.Diagnostics.Activity>
    {
        public readonly List<System.Diagnostics.Activity> Exported = new();
        public override void OnEnd(System.Diagnostics.Activity data)
        {
            Exported.Add(data);
        }
    }
}



