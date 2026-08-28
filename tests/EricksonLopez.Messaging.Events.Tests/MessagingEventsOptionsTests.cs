// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Messaging.Events;
using Xunit;

namespace EricksonLopez.Messaging.Events.Tests;

[Trait("Category", "Unit")]
public sealed class MessagingEventsOptionsTests
{
    [Fact]
    public void Options_Defaults_AreConfiguredProperly()
    {
        var options = new MessagingEventsOptions();

        options.ThrowOnFailure.Should().BeTrue();
        options.DestinationResolver.Should().BeNull();
    }

    [Fact]
    public void Options_CustomValues_CanBeAssigned()
    {
        var options = new MessagingEventsOptions
        {
            ThrowOnFailure = false,
            DestinationResolver = t => $"custom.{t.Name}"
        };

        options.ThrowOnFailure.Should().BeFalse();
        options.DestinationResolver.Should().NotBeNull();
        options.DestinationResolver!(typeof(string)).Should().Be("custom.String");
    }
}
