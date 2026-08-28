// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;

namespace EricksonLopez.Messaging.Tests.Contracts;

using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using FsCheck;
using FsCheck.Xunit;
using NSubstitute;
using Xunit;


[Trait("Category", "Unit")]
public class MessageContextAndOptionsTests
{
    [Fact]
    public void MessageContext_Constructor_ValidParameters_InitializesCorrectly()
    {
        // Arrange
        var metadata = TransportMessageMetadata.Create("test.event");
        var serviceProvider = Substitute.For<IServiceProvider>();
        using var cts = new CancellationTokenSource();

        // Act
        var context = new MessageContext(metadata, serviceProvider, cts.Token);

        // Assert
        context.Metadata.Should().BeSameAs(metadata);
        context.ServiceProvider.Should().BeSameAs(serviceProvider);
        context.CancellationToken.Should().Be(cts.Token);
        context.Message.Should().BeNull();
        context.Items.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void MessageContext_Constructor_NullMetadata_ThrowsArgumentNullException()
    {
        // Arrange
        var sp = Substitute.For<IServiceProvider>();

        // Act
        Action act = () => _ = new MessageContext(null!, sp);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("metadata");
    }

    [Fact]
    public void MessageContext_Constructor_NullServiceProvider_ThrowsArgumentNullException()
    {
        // Arrange
        var metadata = TransportMessageMetadata.Create("test.event");

        // Act
        Action act = () => _ = new MessageContext(metadata, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void MessageContext_MessageProperty_CanBeSetAndRetrieved()
    {
        // Arrange
        var metadata = TransportMessageMetadata.Create("test.event");
        var sp = Substitute.For<IServiceProvider>();
        var context = new MessageContext(metadata, sp);
        var messageObj = new { Text = "Sample Message" };

        // Act
        context.Message = messageObj;

        // Assert
        context.Message.Should().BeSameAs(messageObj);
    }

    [Fact]
    public void MessageContext_ItemsDictionary_IsCaseSensitiveOrdinal()
    {
        // Arrange
        var metadata = TransportMessageMetadata.Create("test.event");
        var sp = Substitute.For<IServiceProvider>();
        var context = new MessageContext(metadata, sp);

        // Act
        context.Items["Key"] = "Value1";
        context.Items["key"] = "Value2";

        // Assert
        context.Items.Should().HaveCount(2);
        context.Items["Key"].Should().Be("Value1");
        context.Items["key"].Should().Be("Value2");
    }

    [Fact]
    public void MessagePublishOptions_GettersAndSetters_WorkCorrectly()
    {
        // Arrange
        var headers = new Dictionary<string, string> { ["h1"] = "v1" };

        // Act
        var options = new MessagePublishOptions
        {
            Destination = "custom-topic",
            CorrelationId = "corr-1",
            CausationId = "cause-1",
            TenantId = "tenant-1",
            PartitionKey = "pk-1",
            Headers = headers
        };

        // Assert
        options.Destination.Should().Be("custom-topic");
        options.CorrelationId.Should().Be("corr-1");
        options.CausationId.Should().Be("cause-1");
        options.TenantId.Should().Be("tenant-1");
        options.PartitionKey.Should().Be("pk-1");
        options.Headers.Should().BeSameAs(headers);
    }

    [Fact]
    public void MessageSendOptions_GettersAndSetters_WorkCorrectly()
    {
        // Arrange
        var headers = new Dictionary<string, string> { ["h2"] = "v2" };

        // Act
        var options = new MessageSendOptions
        {
            CorrelationId = "corr-2",
            CausationId = "cause-2",
            TenantId = "tenant-2",
            PartitionKey = "pk-2",
            Headers = headers
        };

        // Assert
        options.CorrelationId.Should().Be("corr-2");
        options.CausationId.Should().Be("cause-2");
        options.TenantId.Should().Be("tenant-2");
        options.PartitionKey.Should().Be("pk-2");
        options.Headers.Should().BeSameAs(headers);
    }

    [Property]
    public bool MessagePublishOptions_ArbitraryParameters_PreservesValues(
        string? destination,
        string? correlationId,
        string? causationId,
        string? tenantId,
        string? partitionKey)
    {
        var options = new MessagePublishOptions
        {
            Destination = destination,
            CorrelationId = correlationId,
            CausationId = causationId,
            TenantId = tenantId,
            PartitionKey = partitionKey
        };

        return options.Destination == destination &&
               options.CorrelationId == correlationId &&
               options.CausationId == causationId &&
               options.TenantId == tenantId &&
               options.PartitionKey == partitionKey;
    }

    [Property]
    public bool MessageSendOptions_ArbitraryParameters_PreservesValues(
        string? correlationId,
        string? causationId,
        string? tenantId,
        string? partitionKey)
    {
        var options = new MessageSendOptions
        {
            CorrelationId = correlationId,
            CausationId = causationId,
            TenantId = tenantId,
            PartitionKey = partitionKey
        };

        return options.CorrelationId == correlationId &&
               options.CausationId == causationId &&
               options.TenantId == tenantId &&
               options.PartitionKey == partitionKey;
    }
}




