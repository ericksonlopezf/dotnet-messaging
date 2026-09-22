// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.Tests.Diagnostics;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using AwesomeAssertions;
using EricksonLopez.Messaging.Diagnostics;
using Xunit;

[Trait("Category", "Unit")]
public class MessagingDiagnosticsTests
{
    [Fact]
    public void Constants_WhenAccessed_HaveExpectedStandardValues()
    {
        // Assert
        MessagingDiagnostics.ActivitySourceName.Should().Be("EricksonLopez.Messaging");
        MessagingDiagnostics.MeterName.Should().Be("EricksonLopez.Messaging");
        MessagingDiagnostics.Version.Should().Be("2.0.0");
    }

    [Fact]
    public void ActivitySource_WhenAccessed_IsInitializedWithCanonicalNameAndVersion()
    {
        // Assert
        MessagingDiagnostics.ActivitySource.Should().NotBeNull();
        MessagingDiagnostics.ActivitySource.Name.Should().Be("EricksonLopez.Messaging");
        MessagingDiagnostics.ActivitySource.Version.Should().Be("2.0.0");
    }

    [Fact]
    public void Meter_WhenAccessed_IsInitializedWithCanonicalNameAndVersion()
    {
        // Assert
        MessagingDiagnostics.Meter.Should().NotBeNull();
        MessagingDiagnostics.Meter.Name.Should().Be("EricksonLopez.Messaging");
        MessagingDiagnostics.Meter.Version.Should().Be("2.0.0");
    }

    [Fact]
    public void MessagesPublished_WhenAccessed_HasExpectedProperties()
    {
        // Assert
        MessagingDiagnostics.MessagesPublished.Should().NotBeNull();
        MessagingDiagnostics.MessagesPublished.Name.Should().Be("messaging.publish.messages");
        MessagingDiagnostics.MessagesPublished.Unit.Should().Be("messages");
        MessagingDiagnostics.MessagesPublished.Description.Should().Be("Total count of messages published.");
    }

    [Fact]
    public void MessagesReceived_WhenAccessed_HasExpectedProperties()
    {
        // Assert
        MessagingDiagnostics.MessagesReceived.Should().NotBeNull();
        MessagingDiagnostics.MessagesReceived.Name.Should().Be("messaging.receive.messages");
        MessagingDiagnostics.MessagesReceived.Unit.Should().Be("messages");
        MessagingDiagnostics.MessagesReceived.Description.Should().Be("Total count of messages received.");
    }

    [Fact]
    public void MessagesFailed_WhenAccessed_HasExpectedProperties()
    {
        // Assert
        MessagingDiagnostics.MessagesFailed.Should().NotBeNull();
        MessagingDiagnostics.MessagesFailed.Name.Should().Be("messaging.failed.messages");
        MessagingDiagnostics.MessagesFailed.Unit.Should().Be("messages");
        MessagingDiagnostics.MessagesFailed.Description.Should().Be("Total count of failed message processing attempts.");
    }

    [Fact]
    public void ProcessingDuration_WhenAccessed_HasExpectedProperties()
    {
        // Assert
        MessagingDiagnostics.ProcessingDuration.Should().NotBeNull();
        MessagingDiagnostics.ProcessingDuration.Name.Should().Be("messaging.process.duration");
        MessagingDiagnostics.ProcessingDuration.Unit.Should().Be("ms");
        MessagingDiagnostics.ProcessingDuration.Description.Should().Be("Processing duration of messages in milliseconds.");
    }

    [Fact]
    public void MessagesDeduplicated_WhenAccessed_HasExpectedProperties()
    {
        // Assert
        MessagingDiagnostics.MessagesDeduplicated.Should().NotBeNull();
        MessagingDiagnostics.MessagesDeduplicated.Name.Should().Be("messaging.deduplicated.messages");
        MessagingDiagnostics.MessagesDeduplicated.Unit.Should().Be("messages");
        MessagingDiagnostics.MessagesDeduplicated.Description.Should().Be("Total count of duplicate messages detected and skipped.");
    }
}


