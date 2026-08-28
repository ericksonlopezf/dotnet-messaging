// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;

namespace EricksonLopez.Messaging.Tests.Contracts;

using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Tests.Common;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

[Trait("Category", "Unit")]
public class MessageContractTests
{
    private sealed record TestOrderCreated(Guid OrderId, decimal Amount) : IMessage;

    [Fact]
    public void MessageMetadata_Create_WithMinimalParameters_SetsDefaultValues()
    {
        // Act
        var before = DateTimeOffset.UtcNow;
        var metadata = TransportMessageMetadata.Create("orders.created.v1");
        var after = DateTimeOffset.UtcNow;

        // Assert
        metadata.MessageId.Should().NotBeNullOrWhiteSpace();
        metadata.MessageId.Length.Should().Be(32); // Guid "N" format
        metadata.MessageType.Should().Be("orders.created.v1");
        metadata.CorrelationId.Should().NotBeNullOrWhiteSpace();
        metadata.CorrelationId.Length.Should().Be(32);
        metadata.CausationId.Should().BeNull();
        metadata.TraceParent.Should().BeNull();
        metadata.TenantId.Should().BeNull();
        metadata.PartitionKey.Should().BeNull();
        metadata.ContentType.Should().Be("application/json");
        metadata.SchemaVersion.Should().Be(1);
        metadata.Headers.Should().BeNull();
        metadata.Timestamp.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void MessageMetadata_Create_WithAllParameters_PopulatesAllFields()
    {
        // Arrange
        var headers = new Dictionary<string, string> { ["X-Custom"] = "Val" };

        // Act
        var metadata = TransportMessageMetadata.Create(
            messageType: "orders.created.v1",
            correlationId: "corr-123",
            causationId: "cause-456",
            traceParent: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            tenantId: "tenant-a",
            partitionKey: "pk-999",
            schemaVersion: 2,
            headers: headers);

        // Assert
        metadata.MessageId.Should().NotBeNullOrWhiteSpace();
        metadata.MessageType.Should().Be("orders.created.v1");
        metadata.CorrelationId.Should().Be("corr-123");
        metadata.CausationId.Should().Be("cause-456");
        metadata.TraceParent.Should().Be("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
        metadata.TenantId.Should().Be("tenant-a");
        metadata.PartitionKey.Should().Be("pk-999");
        metadata.SchemaVersion.Should().Be(2);
        metadata.ContentType.Should().Be("application/json");
        metadata.Headers.Should().NotBeNull();
        metadata.Headers!["X-Custom"].Should().Be("Val");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void MessageMetadata_Create_NullOrWhiteSpaceMessageType_ThrowsArgumentException(string? invalidType)
    {
        // Act
        Action act = () => TransportMessageMetadata.Create(invalidType!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MessageMetadata_DirectConstructorAndRecordSemantics_WorkCorrectly()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;
        var headers = new Dictionary<string, string> { ["k"] = "v" };
        var metadata = new TransportMessageMetadata(
            MessageId: "m1",
            MessageType: "t1",
            Timestamp: timestamp,
            CorrelationId: "c1",
            CausationId: "cause1",
            TraceParent: "tp1",
            TenantId: "ten1",
            PartitionKey: "pk1",
            ContentType: "application/json",
            SchemaVersion: 3,
            Headers: headers);

        // Assert
        metadata.MessageId.Should().Be("m1");
        metadata.MessageType.Should().Be("t1");
        metadata.Timestamp.Should().Be(timestamp);
        metadata.CorrelationId.Should().Be("c1");
        metadata.CausationId.Should().Be("cause1");
        metadata.TraceParent.Should().Be("tp1");
        metadata.TenantId.Should().Be("ten1");
        metadata.PartitionKey.Should().Be("pk1");
        metadata.ContentType.Should().Be("application/json");
        metadata.SchemaVersion.Should().Be(3);
        metadata.Headers.Should().BeSameAs(headers);

        // With-expression
        var updated = metadata with { CorrelationId = "c2" };
        updated.CorrelationId.Should().Be("c2");
        updated.MessageId.Should().Be("m1");
    }

    [Property]
    public bool MessageMetadata_Create_GeneratesDistinctMessageIds(NonNull<string> typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName.Get)) return true;

        var m1 = TransportMessageMetadata.Create(typeName.Get);
        var m2 = TransportMessageMetadata.Create(typeName.Get);

        return m1.MessageId != m2.MessageId && m1.MessageType == typeName.Get;
    }

    [Fact]
    public void MessageEnvelope_Create_WrapsPayloadAndMetadata()
    {
        // Arrange
        var payload = new TestOrderCreated(Guid.NewGuid(), 250.50m);

        // Act
        var envelope = MessageEnvelope<TestOrderCreated>.Create(
            payload: payload,
            messageType: "orders.created.v1",
            correlationId: "corr-1",
            causationId: "cause-1",
            traceParent: "tp-1",
            tenantId: "t-1",
            partitionKey: "pk-1",
            schemaVersion: 2);

        // Assert
        envelope.Payload.Should().BeSameAs(payload);
        envelope.Metadata.MessageType.Should().Be("orders.created.v1");
        envelope.Metadata.CorrelationId.Should().Be("corr-1");
        envelope.Metadata.CausationId.Should().Be("cause-1");
        envelope.Metadata.TraceParent.Should().Be("tp-1");
        envelope.Metadata.TenantId.Should().Be("t-1");
        envelope.Metadata.PartitionKey.Should().Be("pk-1");
        envelope.Metadata.SchemaVersion.Should().Be(2);
    }

    [Fact]
    public void MessageEnvelope_Create_WithNullPayload_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => MessageEnvelope<TestOrderCreated>.Create(null!, "orders.created.v1");

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MessageEnvelope_DirectConstructorAndRecordSemantics_WorkCorrectly()
    {
        // Arrange
        var payload = new TestOrderCreated(Guid.NewGuid(), 100m);
        var metadata = TransportMessageMetadata.Create("test.type");
        var envelope = new MessageEnvelope<TestOrderCreated>(payload, metadata);

        // Assert
        envelope.Payload.Should().BeSameAs(payload);
        envelope.Metadata.Should().BeSameAs(metadata);

        var newPayload = new TestOrderCreated(Guid.NewGuid(), 200m);
        var updated = envelope with { Payload = newPayload };
        updated.Payload.Should().BeSameAs(newPayload);
        updated.Metadata.Should().BeSameAs(metadata);
    }

    [Fact]
    public void MessageEnvelopeBuilder_WithAllPropertiesConfigured_BuildsEnvelopeCorrectly()
    {
        // Arrange
        var payload = new TestOrderCreated(Guid.NewGuid(), 49.99m);
        var headers = new Dictionary<string, string> { ["Env"] = "Test", ["Region"] = "US" };

        // Act
        var envelope = new MessageEnvelopeBuilder<TestOrderCreated>()
            .WithPayload(payload)
            .WithMessageType("custom.order.v2")
            .WithCorrelationId("corr-custom-99")
            .WithCausationId("cause-custom-88")
            .WithTraceParent("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01")
            .WithTenantId("tenant-custom")
            .WithPartitionKey("pk-custom")
            .WithContentType("application/custom+json")
            .WithSchemaVersion(3)
            .WithHeader("X-Single", "SingleVal")
            .WithHeaders(headers)
            .Build();

        // Assert
        envelope.Should().NotBeNull();
        envelope.Payload.Should().BeSameAs(payload);
        envelope.Metadata.MessageType.Should().Be("custom.order.v2");
        envelope.Metadata.CorrelationId.Should().Be("corr-custom-99");
        envelope.Metadata.CausationId.Should().Be("cause-custom-88");
        envelope.Metadata.TraceParent.Should().Be("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
        envelope.Metadata.TenantId.Should().Be("tenant-custom");
        envelope.Metadata.PartitionKey.Should().Be("pk-custom");
        envelope.Metadata.ContentType.Should().Be("application/custom+json");
        envelope.Metadata.SchemaVersion.Should().Be(3);
        envelope.Metadata.Headers.Should().ContainKey("X-Single").WhoseValue.Should().Be("SingleVal");
        envelope.Metadata.Headers.Should().ContainKey("Env").WhoseValue.Should().Be("Test");
        envelope.Metadata.Headers.Should().ContainKey("Region").WhoseValue.Should().Be("US");
    }

    [Fact]
    public void MessageEnvelopeBuilder_WithoutPayload_ThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new MessageEnvelopeBuilder<TestOrderCreated>()
            .WithMessageType("test.type");

        // Act
        Action act = () => builder.Build();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Payload must be specified before building MessageEnvelope.");
    }

    [Fact]
    public void MessageEnvelopeBuilder_WithNullPayload_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = new MessageEnvelopeBuilder<TestOrderCreated>();

        // Act
        Action act = () => builder.WithPayload(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("payload");
    }

    [Fact]
    public void MessageEnvelopeBuilder_WithNullMessageType_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = new MessageEnvelopeBuilder<TestOrderCreated>();

        // Act
        Action act = () => builder.WithMessageType(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("messageType");
    }

    [Fact]
    public void MessageEnvelopeBuilder_WithNullContentType_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = new MessageEnvelopeBuilder<TestOrderCreated>();

        // Act
        Action act = () => builder.WithContentType(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("contentType");
    }


    [Property]
    public bool MessageMetadata_Create_WithArbitraryParameters_PreservesAllProvidedValues(
        NonNull<string> typeName,
        string? correlationId,
        string? causationId,
        string? traceParent,
        string? tenantId,
        string? partitionKey,
        PositiveInt schemaVersion)
    {
        if (string.IsNullOrWhiteSpace(typeName.Get)) return true;

        var metadata = TransportMessageMetadata.Create(
            messageType: typeName.Get,
            correlationId: correlationId,
            causationId: causationId,
            traceParent: traceParent,
            tenantId: tenantId,
            partitionKey: partitionKey,
            schemaVersion: schemaVersion.Get);

        return metadata.MessageType == typeName.Get &&
               (correlationId == null ? metadata.CorrelationId.Length == 32 : metadata.CorrelationId == correlationId) &&
               metadata.CausationId == causationId &&
               metadata.TraceParent == traceParent &&
               metadata.TenantId == tenantId &&
               metadata.PartitionKey == partitionKey &&
               metadata.SchemaVersion == schemaVersion.Get &&
               metadata.MessageId.Length == 32;
    }

    [Property]
    public bool MessageEnvelopeBuilder_ArbitraryConfiguration_BuildsValidEnvelope(
        NonNull<string> typeName,
        NonNull<string> payloadText,
        NonNull<string> headerKey,
        NonNull<string> headerVal)
    {
        if (string.IsNullOrWhiteSpace(typeName.Get) ||
            string.IsNullOrWhiteSpace(headerKey.Get)) return true;

        var payload = new TestOrderCreated(Guid.NewGuid(), 100m);
        var envelope = new MessageEnvelopeBuilder<TestOrderCreated>()
            .WithPayload(payload)
            .WithMessageType(typeName.Get)
            .WithHeader(headerKey.Get, headerVal.Get)
            .Build();

        return envelope.Payload == payload &&
               envelope.Metadata.MessageType == typeName.Get &&
               envelope.Metadata.Headers != null &&
               envelope.Metadata.Headers.ContainsKey(headerKey.Get) &&
               envelope.Metadata.Headers[headerKey.Get] == headerVal.Get;
    }
}




