// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Tests.Dispatch;

using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Diagnostics;
using EricksonLopez.Messaging.Dispatch;
using EricksonLopez.Messaging.Serialization;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Result;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

[Trait("Category", "Unit")]
public class MessagePublisherTests
{
    [MessageType("order.created.v1")]
    private sealed record OrderCreatedEvent(string OrderId, decimal Amount) : IMessage;

    private sealed record PlainEvent(string Id) : IMessage;

    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();

        Action act1 = () => new MessagePublisher(null!, serializer);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("transport");

        Action act2 = () => new MessagePublisher(transport, null!);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("serializer");
    }

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_NullMessage_ThrowsArgumentNullException()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(transport, serializer);

        Func<Task> act = async () => await publisher.PublishAsync<OrderCreatedEvent>(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishAsync_Success_SerializesAndPublishesWithCustomMessageType()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();

        var evt = new OrderCreatedEvent("ORD-1", 99.99m);
        var expectedBytes = Encoding.UTF8.GetBytes("{\"OrderId\":\"ORD-1\"}");
        serializer.Serialize(evt).Returns(expectedBytes);

        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        var publisher = new MessagePublisher(transport, serializer);

        var options = new MessagePublishOptions
        {
            CorrelationId = "corr-1",
            CausationId = "cause-1",
            TenantId = "tenant-1",
            PartitionKey = "pk-1",
            Headers = new Dictionary<string, string> { ["k1"] = "v1" }
        };

        // Act
        var result = await publisher.PublishAsync(evt, options, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await transport.Received(1).PublishRawAsync(
            "order.created.v1",
            Arg.Is<ReadOnlyMemory<byte>>(b => b.ToArray().Length == expectedBytes.Length),
            Arg.Is<TransportMessageMetadata>(m =>
                m.MessageType == "order.created.v1" &&
                m.CorrelationId == "corr-1" &&
                m.CausationId == "cause-1" &&
                m.TenantId == "tenant-1" &&
                m.PartitionKey == "pk-1" &&
                m.Headers != null && m.Headers["k1"] == "v1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_PlainMessageWithoutAttribute_UsesTypeNameAndCustomDestination()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();

        var evt = new PlainEvent("PL-1");
        serializer.Serialize(evt).Returns(new byte[] { 1 });

        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        var publisher = new MessagePublisher(transport, serializer);
        var options = new MessagePublishOptions { Destination = "custom.topic" };

        // Act
        var result = await publisher.PublishAsync(evt, options);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await transport.Received(1).PublishRawAsync(
            "custom.topic",
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Is<TransportMessageMetadata>(m => m.MessageType == nameof(PlainEvent)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_SerializationThrows_ReturnsFailureResult()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();

        var evt = new OrderCreatedEvent("ORD-1", 10m);
        serializer.Serialize(evt).Throws(new InvalidOperationException("Json cycle detected"));

        var publisher = new MessagePublisher(transport, serializer);

        // Act
        var result = await publisher.PublishAsync(evt);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Messaging.SerializationFailed");
        result.Error.Description.Should().Contain("Json cycle detected");
        await transport.DidNotReceive().PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_TransportReturnsFailure_ReturnsTransportFailureResult()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();

        var evt = new OrderCreatedEvent("ORD-1", 10m);
        serializer.Serialize(evt).Returns(new byte[] { 1 });

        var transportError = Error.Failure("Transport.BrokerDown", "Connection refused");
        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Failure(transportError)));

        var publisher = new MessagePublisher(transport, serializer);

        // Act
        var result = await publisher.PublishAsync(evt);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Transport.BrokerDown");
    }

    #endregion

    #region SendAsync Tests

    [Fact]
    public async Task SendAsync_NullMessage_ThrowsArgumentNullException()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(transport, serializer);

        Func<Task> act = async () => await publisher.SendAsync<OrderCreatedEvent>(null!, "queue");
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendAsync_InvalidDestination_ThrowsArgumentException(string? invalidDest)
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(transport, serializer);

        Func<Task> act = async () => await publisher.SendAsync(new PlainEvent("1"), invalidDest!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendAsync_Success_SerializesAndSendsToSpecifiedDestination()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();

        var evt = new OrderCreatedEvent("ORD-2", 45m);
        serializer.Serialize(evt).Returns(new byte[] { 2, 3 });

        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        var publisher = new MessagePublisher(transport, serializer);
        var options = new MessageSendOptions
        {
            CorrelationId = "corr-send",
            CausationId = "cause-send",
            TenantId = "tenant-send",
            PartitionKey = "pk-send",
            Headers = new Dictionary<string, string> { ["header"] = "value" }
        };

        // Act
        var result = await publisher.SendAsync(evt, "orders.queue", options);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await transport.Received(1).PublishRawAsync(
            "orders.queue",
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Is<TransportMessageMetadata>(m =>
                m.MessageType == "order.created.v1" &&
                m.CorrelationId == "corr-send" &&
                m.CausationId == "cause-send" &&
                m.TenantId == "tenant-send" &&
                m.PartitionKey == "pk-send" &&
                m.Headers != null && m.Headers["header"] == "value"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_SerializationThrows_ReturnsFailureResult()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();

        var evt = new OrderCreatedEvent("ORD-2", 45m);
        serializer.Serialize(evt).Throws(new InvalidOperationException("Serialization crash"));

        var publisher = new MessagePublisher(transport, serializer);

        // Act
        var result = await publisher.SendAsync(evt, "orders.queue");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Messaging.SerializationFailed");
    }

    [Fact]
    public async Task SendAsync_TransportFails_ReturnsFailureResult()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();

        var evt = new OrderCreatedEvent("ORD-2", 45m);
        serializer.Serialize(evt).Returns(new byte[] { 1 });

        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Failure(Error.Failure("Send.Failed", "Queue full"))));

        var publisher = new MessagePublisher(transport, serializer);

        // Act
        var result = await publisher.SendAsync(evt, "orders.queue");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Send.Failed");
    }

    #endregion

    #region PublishBatchAsync & SendBatchAsync Tests

    [Fact]
    public async Task PublishBatchAsync_NullMessages_ThrowsArgumentNullException()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(transport, serializer);

        Func<Task> act = async () => await publisher.PublishBatchAsync<OrderCreatedEvent>(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishBatchAsync_EmptyBatch_ReturnsSuccessWithoutCallingTransport()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(transport, serializer);

        var result = await publisher.PublishBatchAsync(Array.Empty<OrderCreatedEvent>());

        result.IsSuccess.Should().BeTrue();
        await transport.DidNotReceiveWithAnyArgs().PublishRawAsync(default!, default, default!);
    }

    [Fact]
    public async Task PublishBatchAsync_WithBatchTransport_CallsPublishBatchRawAsync()
    {
        var batchTransport = Substitute.For<IBatchMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(batchTransport, serializer);

        var messages = new[] { new OrderCreatedEvent("O1", 10), new OrderCreatedEvent("O2", 20) };
        serializer.Serialize(Arg.Any<OrderCreatedEvent>()).Returns(new byte[] { 1 });

        batchTransport.PublishBatchRawAsync(
            Arg.Is("order.created.v1"),
            Arg.Is<IReadOnlyList<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)>>(b => b.Count == 2),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        var result = await publisher.PublishBatchAsync(messages);

        result.IsSuccess.Should().BeTrue();
        await batchTransport.Received(1).PublishBatchRawAsync(
            Arg.Is("order.created.v1"),
            Arg.Any<IReadOnlyList<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishBatchAsync_WithoutBatchTransport_FallbacksToSequentialPublishRaw()
    {
        var regularTransport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(regularTransport, serializer);

        var messages = new[] { new OrderCreatedEvent("O1", 10), new OrderCreatedEvent("O2", 20) };
        serializer.Serialize(Arg.Any<OrderCreatedEvent>()).Returns(new byte[] { 1 });

        regularTransport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        var result = await publisher.PublishBatchAsync(messages);

        result.IsSuccess.Should().BeTrue();
        await regularTransport.Received(2).PublishRawAsync(
            Arg.Is("order.created.v1"),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendBatchAsync_WithBatchTransport_CallsPublishBatchRawAsyncToDestination()
    {
        var batchTransport = Substitute.For<IBatchMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(batchTransport, serializer);

        var messages = new[] { new OrderCreatedEvent("O1", 10) };
        serializer.Serialize(Arg.Any<OrderCreatedEvent>()).Returns(new byte[] { 1 });

        batchTransport.PublishBatchRawAsync(
            Arg.Is("custom.queue"),
            Arg.Any<IReadOnlyList<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)>>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        var result = await publisher.SendBatchAsync(messages, "custom.queue");

        result.IsSuccess.Should().BeTrue();
        await batchTransport.Received(1).PublishBatchRawAsync(
            Arg.Is("custom.queue"),
            Arg.Any<IReadOnlyList<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)>>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendBatchAsync_InvalidDestination_ThrowsArgumentException(string? invalidDest)
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(transport, serializer);

        var messages = new[] { new OrderCreatedEvent("O1", 10) };

        Func<Task> act = async () => await publisher.SendBatchAsync(messages, invalidDest!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendBatchAsync_NullMessages_ThrowsArgumentNullException()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(transport, serializer);

        Func<Task> act = async () => await publisher.SendBatchAsync<OrderCreatedEvent>(null!, "queue");
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SendBatchAsync_EmptyOrAllNullMessages_ReturnsSuccessWithoutCallingTransport()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(transport, serializer);

        var result1 = await publisher.SendBatchAsync(Array.Empty<OrderCreatedEvent>(), "queue");
        var result2 = await publisher.SendBatchAsync(new OrderCreatedEvent[] { null! }, "queue");

        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        await transport.DidNotReceiveWithAnyArgs().PublishRawAsync(default!, default, default!);
    }

    [Fact]
    public async Task PublishBatchAsync_WithNullItems_FiltersNullItems()
    {
        var batchTransport = Substitute.For<IBatchMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(batchTransport, serializer);

        var messages = new OrderCreatedEvent[] { null!, new OrderCreatedEvent("O1", 10), null! };
        serializer.Serialize(Arg.Any<OrderCreatedEvent>()).Returns(new byte[] { 1 });

        batchTransport.PublishBatchRawAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)>>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        var result = await publisher.PublishBatchAsync(messages);

        result.IsSuccess.Should().BeTrue();
        await batchTransport.Received(1).PublishBatchRawAsync(
            "order.created.v1",
            Arg.Is<IReadOnlyList<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)>>(b => b.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishBatchAsync_SerializationThrows_ReturnsFailureResult()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(transport, serializer);

        var messages = new[] { new OrderCreatedEvent("O1", 10) };
        serializer.Serialize(Arg.Any<OrderCreatedEvent>()).Throws(new InvalidOperationException("Batch serial crash"));

        var result = await publisher.PublishBatchAsync(messages);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Messaging.SerializationFailed");
        result.Error.Description.Should().Contain("Batch serial crash");
    }

    [Fact]
    public async Task SendBatchAsync_SerializationThrows_ReturnsFailureResult()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(transport, serializer);

        var messages = new[] { new OrderCreatedEvent("O1", 10) };
        serializer.Serialize(Arg.Any<OrderCreatedEvent>()).Throws(new InvalidOperationException("Send batch serial crash"));

        var result = await publisher.SendBatchAsync(messages, "queue");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Messaging.SerializationFailed");
        result.Error.Description.Should().Contain("Send batch serial crash");
    }

    [Fact]
    public async Task PublishBatchAsync_BatchTransportFails_ReturnsFailureResult()
    {
        var batchTransport = Substitute.For<IBatchMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(batchTransport, serializer);

        var messages = new[] { new OrderCreatedEvent("O1", 10) };
        serializer.Serialize(Arg.Any<OrderCreatedEvent>()).Returns(new byte[] { 1 });

        batchTransport.PublishBatchRawAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)>>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Failure(Error.Failure("Batch.Failed", "Broker down"))));

        var result = await publisher.PublishBatchAsync(messages);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Batch.Failed");
    }

    [Fact]
    public async Task SendBatchAsync_BatchTransportFails_ReturnsFailureResult()
    {
        var batchTransport = Substitute.For<IBatchMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(batchTransport, serializer);

        var messages = new[] { new OrderCreatedEvent("O1", 10) };
        serializer.Serialize(Arg.Any<OrderCreatedEvent>()).Returns(new byte[] { 1 });

        batchTransport.PublishBatchRawAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)>>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Failure(Error.Failure("SendBatch.Failed", "Broker down"))));

        var result = await publisher.SendBatchAsync(messages, "queue");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SendBatch.Failed");
    }

    [Fact]
    public async Task PublishBatchAsync_SequentialFallback_WhenItemFails_ReturnsFailureAndStops()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(transport, serializer);

        var messages = new[] { new OrderCreatedEvent("O1", 10), new OrderCreatedEvent("O2", 20) };
        serializer.Serialize(Arg.Any<OrderCreatedEvent>()).Returns(new byte[] { 1 });

        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<CancellationToken>())
            .Returns(
                _ => new ValueTask<Result>(Result.Failure(Error.Failure("Item.Failed", "Rejected"))),
                _ => new ValueTask<Result>(Result.Success()));

        var result = await publisher.PublishBatchAsync(messages);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Item.Failed");
        await transport.Received(1).PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendBatchAsync_SequentialFallback_SuccessAndFailure()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(transport, serializer);

        var messages = new[] { new OrderCreatedEvent("O1", 10), new OrderCreatedEvent("O2", 20) };
        serializer.Serialize(Arg.Any<OrderCreatedEvent>()).Returns(new byte[] { 1 });

        // First test: success
        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        var resultSuccess = await publisher.SendBatchAsync(messages, "queue");
        resultSuccess.IsSuccess.Should().BeTrue();

        // Second test: item 1 fails, item 2 would succeed (verifies break stops processing)
        var transport2 = Substitute.For<IMessageTransport>();
        var publisher2 = new MessagePublisher(transport2, serializer);
        transport2.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<CancellationToken>())
            .Returns(
                _ => new ValueTask<Result>(Result.Failure(Error.Failure("SendItem.Failed", "Rejected"))),
                _ => new ValueTask<Result>(Result.Success()));

        var resultFailure = await publisher2.SendBatchAsync(messages, "queue");
        resultFailure.IsFailure.Should().BeTrue();
        resultFailure.Error.Code.Should().Be("SendItem.Failed");
        await transport2.Received(1).PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishBatchAsync_WithCustomDestinationInOptions_UsesCustomDestination()
    {
        var batchTransport = Substitute.For<IBatchMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(batchTransport, serializer);

        var messages = new[] { new OrderCreatedEvent("O1", 10) };
        serializer.Serialize(Arg.Any<OrderCreatedEvent>()).Returns(new byte[] { 1 });

        batchTransport.PublishBatchRawAsync(
            Arg.Is("custom.batch.topic"),
            Arg.Any<IReadOnlyList<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)>>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        var result = await publisher.PublishBatchAsync(messages, new MessagePublishOptions { Destination = "custom.batch.topic" });

        result.IsSuccess.Should().BeTrue();
        await batchTransport.Received(1).PublishBatchRawAsync(
            "custom.batch.topic",
            Arg.Any<IReadOnlyList<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TelemetryAndMetrics_PublishAndSend_RecordsActivitiesAndMetrics()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(transport, serializer);

        var evt = new OrderCreatedEvent("O1", 10);
        serializer.Serialize(evt).Returns(new byte[] { 1 });

        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "EricksonLopez.Messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = act => activities.Add(act)
        };
        ActivitySource.AddActivityListener(listener);

        using var meterListener = new System.Diagnostics.Metrics.MeterListener();
        long publishedCount = 0;
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "EricksonLopez.Messaging" && instrument.Name == "messaging.publish.messages")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "messaging.publish.messages")
            {
                publishedCount += measurement;
            }
        });
        meterListener.Start();

        var beforeCount = publishedCount;

        var result = await publisher.PublishAsync(evt, new MessagePublishOptions { CorrelationId = "corr-1" });
        result.IsSuccess.Should().BeTrue();

        var resultSend = await publisher.SendAsync(evt, "target.queue", new MessageSendOptions { CorrelationId = "corr-send" });
        resultSend.IsSuccess.Should().BeTrue();

        var resultBatch = await publisher.PublishBatchAsync(new[] { evt, evt });
        resultBatch.IsSuccess.Should().BeTrue();

        var resultSendBatch = await publisher.SendBatchAsync(new[] { evt }, "target.queue");
        resultSendBatch.IsSuccess.Should().BeTrue();

        (publishedCount - beforeCount).Should().BeGreaterThanOrEqualTo(5); // 1 + 1 + 2 + 1 = 5 (or more if concurrent tests incremented)

        var snapshot = activities.ToArray();
        var pubAct = snapshot.Single(a => a.OperationName == "order.created.v1 publish");
        pubAct.Status.Should().Be(ActivityStatusCode.Ok);
        pubAct.GetTagItem("messaging.system").Should().Be("ericksonlopez.messaging");
        pubAct.GetTagItem("messaging.destination.name").Should().Be("order.created.v1");
        pubAct.GetTagItem("messaging.operation").Should().Be("publish");
        pubAct.GetTagItem("messaging.message.id").Should().NotBeNull();
        pubAct.GetTagItem("messaging.message.type").Should().Be("order.created.v1");
        pubAct.GetTagItem("messaging.message.conversation_id").Should().Be("corr-1");

        var sendAct = snapshot.Single(a => a.OperationName == "target.queue send");
        sendAct.Status.Should().Be(ActivityStatusCode.Ok);
        sendAct.GetTagItem("messaging.system").Should().Be("ericksonlopez.messaging");
        sendAct.GetTagItem("messaging.destination.name").Should().Be("target.queue");
        sendAct.GetTagItem("messaging.operation").Should().Be("send");
        sendAct.GetTagItem("messaging.message.id").Should().NotBeNull();
        sendAct.GetTagItem("messaging.message.type").Should().Be("order.created.v1");
        sendAct.GetTagItem("messaging.message.conversation_id").Should().Be("corr-send");

        var batchPubAct = snapshot.Single(a => a.OperationName == "order.created.v1 publish_batch");
        batchPubAct.Status.Should().Be(ActivityStatusCode.Ok);
        batchPubAct.GetTagItem("messaging.system").Should().Be("ericksonlopez.messaging");
        batchPubAct.GetTagItem("messaging.destination.name").Should().Be("order.created.v1");
        batchPubAct.GetTagItem("messaging.operation").Should().Be("publish_batch");
        batchPubAct.GetTagItem("messaging.batch.count").Should().Be(2);

        var batchSendAct = snapshot.Single(a => a.OperationName == "target.queue send_batch");
        batchSendAct.Status.Should().Be(ActivityStatusCode.Ok);
        batchSendAct.GetTagItem("messaging.system").Should().Be("ericksonlopez.messaging");
        batchSendAct.GetTagItem("messaging.destination.name").Should().Be("target.queue");
        batchSendAct.GetTagItem("messaging.operation").Should().Be("send_batch");
        batchSendAct.GetTagItem("messaging.batch.count").Should().Be(1);
    }

    [Fact]
    public async Task Telemetry_WhenPublishAndSendFail_SetsActivityStatusError()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(transport, serializer);

        var evt = new OrderCreatedEvent("O1", 10);
        serializer.Serialize(evt).Returns(new byte[] { 1 });

        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<TransportMessageMetadata>(),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Failure(Error.Failure("Err.Publish", "Broker refused"))));

        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "EricksonLopez.Messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = act => activities.Add(act)
        };
        ActivitySource.AddActivityListener(listener);

        var result = await publisher.PublishAsync(evt);
        result.IsFailure.Should().BeTrue();

        var resultSend = await publisher.SendAsync(evt, "target.queue");
        resultSend.IsFailure.Should().BeTrue();

        var resultBatch = await publisher.PublishBatchAsync(new[] { evt });
        resultBatch.IsFailure.Should().BeTrue();

        var resultSendBatch = await publisher.SendBatchAsync(new[] { evt }, "target.queue");
        resultSendBatch.IsFailure.Should().BeTrue();

        var snapshot = activities.ToArray();
        snapshot.Should().Contain(a => a.OperationName == "order.created.v1 publish" && a.Status == ActivityStatusCode.Error && a.StatusDescription == "Broker refused");
        snapshot.Should().Contain(a => a.OperationName == "target.queue send" && a.Status == ActivityStatusCode.Error && a.StatusDescription == "Broker refused");
        snapshot.Should().Contain(a => a.OperationName == "order.created.v1 publish_batch" && a.Status == ActivityStatusCode.Error && a.StatusDescription == "Broker refused");
        snapshot.Should().Contain(a => a.OperationName == "target.queue send_batch" && a.Status == ActivityStatusCode.Error && a.StatusDescription == "Broker refused");
    }

    [Fact]
    public async Task Telemetry_WhenSerializationFails_SetsActivityStatusError()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(transport, serializer);

        var evt = new OrderCreatedEvent("O1", 10);
        serializer.Serialize(evt).Throws(new InvalidOperationException("Boom"));

        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "EricksonLopez.Messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = act => activities.Add(act)
        };
        ActivitySource.AddActivityListener(listener);

        var result = await publisher.PublishAsync(evt);
        result.IsFailure.Should().BeTrue();

        var resultSend = await publisher.SendAsync(evt, "target.queue");
        resultSend.IsFailure.Should().BeTrue();

        var snapshot = activities.ToArray();
        snapshot.Should().Contain(a => a.OperationName == "order.created.v1 publish" && a.Status == ActivityStatusCode.Error && a.StatusDescription == "Boom");
        snapshot.Should().Contain(a => a.OperationName == "target.queue send" && a.Status == ActivityStatusCode.Error && a.StatusDescription == "Boom");
    }

    [Fact]
    public async Task PublishAndSend_WithActiveTraceParent_AndFullOptions_PropagatesActivityIdAndOptions()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        var publisher = new MessagePublisher(transport, serializer);

        var evt = new OrderCreatedEvent("O1", 10);
        serializer.Serialize(evt).Returns(new byte[] { 1 });

        var capturedMetadataList = new List<TransportMessageMetadata>();
        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Do<TransportMessageMetadata>(m => capturedMetadataList.Add(m)),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        using var parentActivity = new Activity("Parent").Start();

        var pubOpts = new MessagePublishOptions
        {
            CorrelationId = "c1",
            CausationId = "caus1",
            TenantId = "t1",
            PartitionKey = "pk1",
            Headers = new Dictionary<string, string> { ["h1"] = "v1" }
        };

        var sendOpts = new MessageSendOptions
        {
            CorrelationId = "c2",
            CausationId = "caus2",
            TenantId = "t2",
            PartitionKey = "pk2",
            Headers = new Dictionary<string, string> { ["h2"] = "v2" }
        };

        var r1 = await publisher.PublishAsync(evt, pubOpts);
        var r2 = await publisher.SendAsync(evt, "q", sendOpts);
        var r3 = await publisher.PublishBatchAsync(new[] { evt }, pubOpts);
        var r4 = await publisher.SendBatchAsync(new[] { evt }, "q", sendOpts);

        r1.IsSuccess.Should().BeTrue();
        r2.IsSuccess.Should().BeTrue();
        r3.IsSuccess.Should().BeTrue();
        r4.IsSuccess.Should().BeTrue();

        capturedMetadataList.Should().HaveCount(4);
        foreach (var meta in capturedMetadataList)
        {
            meta.TraceParent.Should().NotBeNull();
            meta.TraceParent.Should().StartWith($"00-{parentActivity.TraceId}");
        }
        capturedMetadataList[0].CorrelationId.Should().Be("c1");
        capturedMetadataList[1].CorrelationId.Should().Be("c2");
        capturedMetadataList[2].CorrelationId.Should().Be("c1");
        capturedMetadataList[3].CorrelationId.Should().Be("c2");
    }

    private sealed class TrackingSynchronizationContext : SynchronizationContext
    {
        public int PostCount;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref PostCount);
            ThreadPool.QueueUserWorkItem(_ => d(state));
        }
    }

    [Fact]
    public void PublishAndSend_ConfiguresAwaitFalse_DoesNotCaptureSynchronizationContext()
    {
        var syncContext = new TrackingSynchronizationContext();
        var prevContext = SynchronizationContext.Current;
        try
        {
            var publishTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
            var batchTcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

            var batchTransport = Substitute.For<IBatchMessageTransport>();
            batchTransport.PublishRawAsync(
                Arg.Any<string>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<TransportMessageMetadata>(),
                Arg.Any<CancellationToken>())
                .Returns(_ => new ValueTask<Result>(publishTcs.Task));

            batchTransport.PublishBatchRawAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)>>(),
                Arg.Any<CancellationToken>())
                .Returns(_ => new ValueTask<Result>(batchTcs.Task));

            var serializer = Substitute.For<IMessageSerializer>();
            serializer.Serialize(Arg.Any<OrderCreatedEvent>()).Returns(new byte[] { 1 });

            var publisher = new MessagePublisher(batchTransport, serializer);
            var evt = new OrderCreatedEvent("O1", 10);

            SynchronizationContext.SetSynchronizationContext(syncContext);

            var v1 = publisher.PublishAsync(evt);
            var v2 = publisher.SendAsync(evt, "queue");
            var v3 = publisher.PublishBatchAsync(new[] { evt });
            var v4 = publisher.SendBatchAsync(new[] { evt }, "queue");

            SynchronizationContext.SetSynchronizationContext(prevContext);

#pragma warning disable xUnit1031
            Task.Run(() =>
            {
                publishTcs.SetResult(Result.Success());
                batchTcs.SetResult(Result.Success());
            }).Wait();

            v1.GetAwaiter().GetResult();
            v2.GetAwaiter().GetResult();
            v3.GetAwaiter().GetResult();
            v4.GetAwaiter().GetResult();
#pragma warning restore xUnit1031

            syncContext.PostCount.Should().Be(0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevContext);
        }
    }

    [Fact]
    public void PublishAndSend_SequentialFallback_ConfiguresAwaitFalse_DoesNotCaptureSynchronizationContext()
    {
        var syncContext = new TrackingSynchronizationContext();
        var prevContext = SynchronizationContext.Current;
        try
        {
            var itemTcs1 = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
            var itemTcs2 = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);

            var regularTransport = Substitute.For<IMessageTransport>();
            regularTransport.PublishRawAsync(
                Arg.Any<string>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<TransportMessageMetadata>(),
                Arg.Any<CancellationToken>())
                .Returns(
                    _ => new ValueTask<Result>(itemTcs1.Task),
                    _ => new ValueTask<Result>(itemTcs2.Task));

            var serializer = Substitute.For<IMessageSerializer>();
            serializer.Serialize(Arg.Any<OrderCreatedEvent>()).Returns(new byte[] { 1 });

            var publisher = new MessagePublisher(regularTransport, serializer);
            var evt = new OrderCreatedEvent("O1", 10);

            SynchronizationContext.SetSynchronizationContext(syncContext);

            var v1 = publisher.PublishBatchAsync(new[] { evt });
            var v2 = publisher.SendBatchAsync(new[] { evt }, "queue");

            SynchronizationContext.SetSynchronizationContext(prevContext);

#pragma warning disable xUnit1031
            Task.Run(() =>
            {
                itemTcs1.SetResult(Result.Success());
                itemTcs2.SetResult(Result.Success());
            }).Wait();

            v1.GetAwaiter().GetResult();
            v2.GetAwaiter().GetResult();
#pragma warning restore xUnit1031

            syncContext.PostCount.Should().Be(0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevContext);
        }
    }

    [Fact]
    public async Task PublishAsync_WithPartitionKeyResolvers_UsesFirstMatchingResolver()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Serialize(Arg.Any<PlainEvent>()).Returns(new byte[] { 1 });

        TransportMessageMetadata? captured = null;
        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Do<TransportMessageMetadata>(m => captured = m),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        var resolver1 = Substitute.For<IPartitionKeyResolver>();
        resolver1.Resolve(Arg.Any<object>()).Returns((string?)null);

        var resolver2 = Substitute.For<IPartitionKeyResolver>();
        resolver2.Resolve(Arg.Any<object>()).Returns("from-resolver-2");

        var publisher = new MessagePublisher(transport, serializer, new[] { resolver1, resolver2 });
        var result = await publisher.PublishAsync(new PlainEvent("item-1"));

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.PartitionKey.Should().Be("from-resolver-2");
    }

    [Fact]
    public async Task PublishAsync_WithOptionsPartitionKey_OverridesResolvers()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Serialize(Arg.Any<PlainEvent>()).Returns(new byte[] { 1 });

        TransportMessageMetadata? captured = null;
        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Do<TransportMessageMetadata>(m => captured = m),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        var resolver = Substitute.For<IPartitionKeyResolver>();
        resolver.Resolve(Arg.Any<object>()).Returns("from-resolver");

        var publisher = new MessagePublisher(transport, serializer, new[] { resolver });
        var options = new MessagePublishOptions { PartitionKey = "options-key" };
        var result = await publisher.PublishAsync(new PlainEvent("item-1"), options);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.PartitionKey.Should().Be("options-key");
    }

    [Fact]
    public async Task SendAsync_WithOptionsPartitionKey_OverridesResolvers()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Serialize(Arg.Any<PlainEvent>()).Returns(new byte[] { 1 });

        TransportMessageMetadata? captured = null;
        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Do<TransportMessageMetadata>(m => captured = m),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        var resolver = Substitute.For<IPartitionKeyResolver>();
        resolver.Resolve(Arg.Any<object>()).Returns("from-resolver");

        var publisher = new MessagePublisher(transport, serializer, new[] { resolver });
        var options = new MessageSendOptions { PartitionKey = "options-send-key" };
        var result = await publisher.SendAsync(new PlainEvent("item-1"), "queue-1", options);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.PartitionKey.Should().Be("options-send-key");
    }

    [Fact]
    public async Task PublishBatchAsync_WithOptionsPartitionKey_OverridesResolvers()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Serialize(Arg.Any<PlainEvent>()).Returns(new byte[] { 1 });

        TransportMessageMetadata? captured = null;
        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Do<TransportMessageMetadata>(m => captured = m),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        var resolver = Substitute.For<IPartitionKeyResolver>();
        resolver.Resolve(Arg.Any<object>()).Returns("from-resolver");

        var publisher = new MessagePublisher(transport, serializer, new[] { resolver });
        var options = new MessagePublishOptions { PartitionKey = "options-batch-key" };
        var result = await publisher.PublishBatchAsync(new[] { new PlainEvent("item-1") }, options);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.PartitionKey.Should().Be("options-batch-key");
    }

    [Fact]
    public async Task SendBatchAsync_WithOptionsPartitionKey_OverridesResolvers()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Serialize(Arg.Any<PlainEvent>()).Returns(new byte[] { 1 });

        TransportMessageMetadata? captured = null;
        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Do<TransportMessageMetadata>(m => captured = m),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        var resolver = Substitute.For<IPartitionKeyResolver>();
        resolver.Resolve(Arg.Any<object>()).Returns("from-resolver");

        var publisher = new MessagePublisher(transport, serializer, new[] { resolver });
        var options = new MessageSendOptions { PartitionKey = "options-sendbatch-key" };
        var result = await publisher.SendBatchAsync(new[] { new PlainEvent("item-1") }, "queue-1", options);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.PartitionKey.Should().Be("options-sendbatch-key");
    }

    [Fact]
    public async Task PublishAsync_WithAmbientActivityAndNoListener_PropagatesAmbientActivityId()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Serialize(Arg.Any<PlainEvent>()).Returns(new byte[] { 1 });

        TransportMessageMetadata? captured = null;
        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Do<TransportMessageMetadata>(m => captured = m),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        using var ambient = new Activity("ambient-publish");
        ambient.SetIdFormat(ActivityIdFormat.W3C);
        ambient.Start();

        var publisher = new MessagePublisher(transport, serializer);
        var result = await publisher.PublishAsync(new PlainEvent("item-ambient"));

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.TraceParent.Should().NotBeNull();
        captured.TraceParent.Should().Contain(ambient.TraceId.ToString());
    }

    [Fact]
    public async Task SendAsync_WithOptionsNullPartitionKey_FallsBackToResolvedPartitionKey()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Serialize(Arg.Any<PlainEvent>()).Returns(new byte[] { 1 });

        TransportMessageMetadata? captured = null;
        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Do<TransportMessageMetadata>(m => captured = m),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        var resolver = Substitute.For<IPartitionKeyResolver>();
        resolver.Resolve(Arg.Any<object>()).Returns("resolver-key-send");

        var publisher = new MessagePublisher(transport, serializer, new[] { resolver });
        var options = new MessageSendOptions { PartitionKey = null };
        var result = await publisher.SendAsync(new PlainEvent("item-1"), "queue-1", options);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.PartitionKey.Should().Be("resolver-key-send");
    }

    [Fact]
    public async Task PublishBatchAsync_WithOptionsNullPartitionKey_FallsBackToResolvedPartitionKey()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Serialize(Arg.Any<PlainEvent>()).Returns(new byte[] { 1 });

        TransportMessageMetadata? captured = null;
        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Do<TransportMessageMetadata>(m => captured = m),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        var resolver = Substitute.For<IPartitionKeyResolver>();
        resolver.Resolve(Arg.Any<object>()).Returns("resolver-key-pubbatch");

        var publisher = new MessagePublisher(transport, serializer, new[] { resolver });
        var options = new MessagePublishOptions { PartitionKey = null };
        var result = await publisher.PublishBatchAsync(new[] { new PlainEvent("item-1") }, options);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.PartitionKey.Should().Be("resolver-key-pubbatch");
    }

    [Fact]
    public async Task SendBatchAsync_WithOptionsNullPartitionKey_FallsBackToResolvedPartitionKey()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Serialize(Arg.Any<PlainEvent>()).Returns(new byte[] { 1 });

        TransportMessageMetadata? captured = null;
        transport.PublishRawAsync(
            Arg.Any<string>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Do<TransportMessageMetadata>(m => captured = m),
            Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Success()));

        var resolver = Substitute.For<IPartitionKeyResolver>();
        resolver.Resolve(Arg.Any<object>()).Returns("resolver-key-sendbatch");

        var publisher = new MessagePublisher(transport, serializer, new[] { resolver });
        var options = new MessageSendOptions { PartitionKey = null };
        var result = await publisher.SendBatchAsync(new[] { new PlainEvent("item-1") }, "queue-1", options);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.PartitionKey.Should().Be("resolver-key-sendbatch");
    }

    [Fact]
    public async Task PublishBatchAsync_WhenSerializationFails_SetsActivityErrorStatus()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Serialize(Arg.Any<OrderCreatedEvent>()).Returns(_ => throw new InvalidOperationException("Serialization boom"));

        Activity? stoppedActivity = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "EricksonLopez.Messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = act => stoppedActivity = act
        };
        ActivitySource.AddActivityListener(listener);

        var publisher = new MessagePublisher(transport, serializer);
        var result = await publisher.PublishBatchAsync(new[] { new OrderCreatedEvent("O1", 10) });

        result.IsFailure.Should().BeTrue();
        stoppedActivity.Should().NotBeNull();
        stoppedActivity!.Status.Should().Be(ActivityStatusCode.Error);
        stoppedActivity.StatusDescription.Should().Be("Serialization boom");
    }

    [Fact]
    public async Task SendBatchAsync_WhenSerializationFails_SetsActivityErrorStatus()
    {
        var transport = Substitute.For<IMessageTransport>();
        var serializer = Substitute.For<IMessageSerializer>();
        serializer.Serialize(Arg.Any<OrderCreatedEvent>()).Returns(_ => throw new InvalidOperationException("SendBatch serialization boom"));

        Activity? stoppedActivity = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "EricksonLopez.Messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = act => stoppedActivity = act
        };
        ActivitySource.AddActivityListener(listener);

        var publisher = new MessagePublisher(transport, serializer);
        var result = await publisher.SendBatchAsync(new[] { new OrderCreatedEvent("O1", 10) }, "queue");

        result.IsFailure.Should().BeTrue();
        stoppedActivity.Should().NotBeNull();
        stoppedActivity!.Status.Should().Be(ActivityStatusCode.Error);
        stoppedActivity.StatusDescription.Should().Be("SendBatch serialization boom");
    }

    #endregion
}






