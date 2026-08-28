// Copyright © Erickson Lopez. MIT License.
using System;
using System.Text;
using System.Threading;
using AwesomeAssertions;
using Azure.Messaging.ServiceBus;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Messaging.AzureServiceBus.Tests.Common;

[Trait("Category", "Transport")]
public class AzureServiceBusTestFactoryTests
{
    [Fact]
    public void CreateProcessMessageEventArgs_ValidParameters_ReturnsValidInstance()
    {
        // Arrange
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData(Encoding.UTF8.GetBytes("test-payload")),
            messageId: "msg-123",
            correlationId: "corr-123");
        var receiver = Substitute.For<ServiceBusReceiver>();
        using var cts = new CancellationTokenSource();

        // Act
        var args = AzureServiceBusTestFactory.CreateProcessMessageEventArgs(message, receiver, cts.Token);

        // Assert
        args.Should().NotBeNull();
        args.Message.Should().BeSameAs(message);
        args.CancellationToken.Should().Be(cts.Token);
    }
}
