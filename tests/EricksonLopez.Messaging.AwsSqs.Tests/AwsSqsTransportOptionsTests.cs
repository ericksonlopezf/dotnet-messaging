// Copyright © Erickson Lopez. MIT License.
using AwesomeAssertions;
using EricksonLopez.Messaging.Transport.AwsSqs;
using Xunit;

namespace EricksonLopez.Messaging.AwsSqs.Tests;

[Trait("Category", "Unit")]
public class AwsSqsTransportOptionsTests
{
    [Fact]
    public void AwsSqsTransportOptions_DefaultValues_AreCorrect()
    {
        var options = new AwsSqsTransportOptions();

        options.Region.Should().Be("us-east-1");
        options.ServiceUrl.Should().BeNull();
        options.WaitTimeSeconds.Should().Be(20);
        options.MaxNumberOfMessages.Should().Be(10);
    }

    [Fact]
    public void AwsSqsTransportOptions_CustomValues_CanBeAssigned()
    {
        var options = new AwsSqsTransportOptions
        {
            Region = "eu-west-1",
            ServiceUrl = "http://localhost:4566",
            WaitTimeSeconds = 5,
            MaxNumberOfMessages = 3
        };

        options.Region.Should().Be("eu-west-1");
        options.ServiceUrl.Should().Be("http://localhost:4566");
        options.WaitTimeSeconds.Should().Be(5);
        options.MaxNumberOfMessages.Should().Be(3);
    }
}
