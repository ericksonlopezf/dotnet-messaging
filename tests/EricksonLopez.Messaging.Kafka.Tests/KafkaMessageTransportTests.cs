// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Confluent.Kafka;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Messaging.Transport.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace EricksonLopez.Messaging.Kafka.Tests;

using TransportMessageMetadata = EricksonLopez.Messaging.Contracts.TransportMessageMetadata;

[Trait("Category", "Unit")]
public class KafkaMessageTransportTests
{
    #region Constructor

    [Fact]
    public void Constructor_Default_InitializesDefaults()
    {
        using var transport = new KafkaMessageTransport();
        transport.Should().NotBeNull();
        transport.Should().BeAssignableTo<IAsyncDisposable>();
    }

    [Fact]
    public void Constructor_WithOptions_InitializesProducerWithConfig()
    {
        var options = Options.Create(new KafkaTransportOptions
        {
            BootstrapServers = "kafka.custom:9092",
            ClientId = "test-client"
        });

        using var transport = new KafkaMessageTransport(options: options);
        transport.Should().NotBeNull();
    }

    [Fact]
    public async Task Constructor_WithExplicitInstances_UsesProvidedInstances()
    {
        var producer = Substitute.For<IProducer<string, byte[]>>();
        producer.ProduceAsync(Arg.Any<string>(), Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DeliveryResult<string, byte[]> { Status = PersistenceStatus.Persisted }));
        var logger = Substitute.For<ILogger<KafkaMessageTransport>>();
        Func<ConsumerConfig, IConsumer<string, byte[]>> factory = _ => Substitute.For<IConsumer<string, byte[]>>();

        using var transport = new KafkaMessageTransport(producer: producer, consumerFactory: factory, logger: logger);
        var result = await transport.PublishRawAsync("topic", new byte[] { 1 }, TransportMessageMetadata.Create("test"));

        result.IsSuccess.Should().BeTrue();
        await producer.Received(1).ProduceAsync("topic", Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region PublishRawAsync

    [Fact]
    public async Task PublishRawAsync_Disposed_ThrowsObjectDisposedException()
    {
        var producer = Substitute.For<IProducer<string, byte[]>>();
        var transport = new KafkaMessageTransport(producer: producer);
        transport.Dispose();

        Func<Task> act = async () => await transport.PublishRawAsync("topic-1", new byte[] { 1 }, TransportMessageMetadata.Create("test"));
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PublishRawAsync_InvalidDestination_ThrowsArgumentException(string? destination)
    {
        var producer = Substitute.For<IProducer<string, byte[]>>();
        using var transport = new KafkaMessageTransport(producer: producer);

        Func<Task> act = async () => await transport.PublishRawAsync(destination!, new byte[] { 1 }, TransportMessageMetadata.Create("test"));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PublishRawAsync_NullMetadata_ThrowsArgumentNullException()
    {
        var producer = Substitute.For<IProducer<string, byte[]>>();
        using var transport = new KafkaMessageTransport(producer: producer);

        Func<Task> act = async () => await transport.PublishRawAsync("topic-1", new byte[] { 1 }, null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("metadata");
    }

    [Fact]
    public async Task PublishRawAsync_ValidMessageWithRichMetadata_ProducesKafkaMessageWithAllHeadersAndKey()
    {
        var producer = Substitute.For<IProducer<string, byte[]>>();
        Message<string, byte[]>? capturedMsg = null;
        string? capturedTopic = null;

        producer.ProduceAsync(
            Arg.Do<string>(t => capturedTopic = t),
            Arg.Do<Message<string, byte[]>>(m => capturedMsg = m),
            Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult<string, byte[]>
            {
                Status = PersistenceStatus.Persisted,
                Topic = "topic-orders"
            });

        using var transport = new KafkaMessageTransport(producer: producer);
        var rawPayload = Encoding.UTF8.GetBytes("{\"orderId\":99}");
        var metadata = new TransportMessageMetadata(
            MessageId: "msg-k-1",
            MessageType: "OrderPlaced",
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: "corr-k-1",
            CausationId: "cause-k-1",
            TraceParent: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            TenantId: "tenant-k-1",
            PartitionKey: "user-42",
            ContentType: "application/custom+json",
            SchemaVersion: 3,
            Headers: new Dictionary<string, string> { ["x-custom-kafka"] = "kafka-header-val" });

        var result = await transport.PublishRawAsync("topic-orders", rawPayload, metadata);

        result.IsSuccess.Should().BeTrue();
        capturedTopic.Should().Be("topic-orders");
        capturedMsg.Should().NotBeNull();
        capturedMsg!.Key.Should().Be("user-42");
        capturedMsg.Value.Should().BeEquivalentTo(rawPayload);

        capturedMsg.Headers.TryGetLastBytes("message-id", out var idBytes).Should().BeTrue();
        Encoding.UTF8.GetString(idBytes!).Should().Be("msg-k-1");

        capturedMsg.Headers.TryGetLastBytes("message-type", out var typeBytes).Should().BeTrue();
        Encoding.UTF8.GetString(typeBytes!).Should().Be("OrderPlaced");

        capturedMsg.Headers.TryGetLastBytes("correlation-id", out var corrBytes).Should().BeTrue();
        Encoding.UTF8.GetString(corrBytes!).Should().Be("corr-k-1");

        capturedMsg.Headers.TryGetLastBytes("tenant-id", out var tenantBytes).Should().BeTrue();
        Encoding.UTF8.GetString(tenantBytes!).Should().Be("tenant-k-1");

        capturedMsg.Headers.TryGetLastBytes("traceparent", out var traceBytes).Should().BeTrue();
        Encoding.UTF8.GetString(traceBytes!).Should().Be("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");

        capturedMsg.Headers.TryGetLastBytes("causation-id", out var causeBytes).Should().BeTrue();
        Encoding.UTF8.GetString(causeBytes!).Should().Be("cause-k-1");

        capturedMsg.Headers.TryGetLastBytes("content-type", out var ctBytes).Should().BeTrue();
        Encoding.UTF8.GetString(ctBytes!).Should().Be("application/custom+json");

        capturedMsg.Headers.TryGetLastBytes("schema-version", out var svBytes).Should().BeTrue();
        Encoding.UTF8.GetString(svBytes!).Should().Be("3");

        capturedMsg.Headers.TryGetLastBytes("x-custom-kafka", out var custBytes).Should().BeTrue();
        Encoding.UTF8.GetString(custBytes!).Should().Be("kafka-header-val");
    }

    [Fact]
    public async Task PublishRawAsync_PartitionKeyNull_UsesMessageIdAsKey()
    {
        var producer = Substitute.For<IProducer<string, byte[]>>();
        Message<string, byte[]>? capturedMsg = null;

        producer.ProduceAsync(Arg.Any<string>(), Arg.Do<Message<string, byte[]>>(m => capturedMsg = m), Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult<string, byte[]>
            {
                Status = PersistenceStatus.Persisted,
                Topic = "topic-1"
            });

        using var transport = new KafkaMessageTransport(producer: producer);
        var metadata = new TransportMessageMetadata(
            MessageId: "msg-unique-id",
            MessageType: "TestType",
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: string.Empty,
            CausationId: null,
            TraceParent: null,
            TenantId: null,
            PartitionKey: null,
            ContentType: "application/json",
            SchemaVersion: 1,
            Headers: null);

        var result = await transport.PublishRawAsync("topic-1", new byte[] { 1 }, metadata);

        result.IsSuccess.Should().BeTrue();
        capturedMsg.Should().NotBeNull();
        capturedMsg!.Key.Should().Be("msg-unique-id");
    }

    [Fact]
    public async Task PublishRawAsync_PartitionKeyAndMessageIdEmpty_GeneratesGuidKey()
    {
        var producer = Substitute.For<IProducer<string, byte[]>>();
        Message<string, byte[]>? capturedMsg = null;

        producer.ProduceAsync(Arg.Any<string>(), Arg.Do<Message<string, byte[]>>(m => capturedMsg = m), Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult<string, byte[]>
            {
                Status = PersistenceStatus.Persisted,
                Topic = "topic-1"
            });

        using var transport = new KafkaMessageTransport(producer: producer);
        var metadata = new TransportMessageMetadata(
            MessageId: string.Empty,
            MessageType: string.Empty,
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: string.Empty,
            CausationId: null,
            TraceParent: null,
            TenantId: null,
            PartitionKey: null,
            ContentType: string.Empty,
            SchemaVersion: 1,
            Headers: null);

        var result = await transport.PublishRawAsync("topic-1", new byte[] { 1 }, metadata);

        result.IsSuccess.Should().BeTrue();
        capturedMsg.Should().NotBeNull();
        capturedMsg!.Key.Should().NotBeNullOrWhiteSpace();
        capturedMsg.Key.Length.Should().Be(32);
        Guid.TryParseExact(capturedMsg.Key, "N", out _).Should().BeTrue();
        capturedMsg.Headers.Count.Should().Be(0);
    }

    [Fact]
    public async Task PublishRawAsync_WhenNotPersisted_ReturnsFailureResult()
    {
        var producer = Substitute.For<IProducer<string, byte[]>>();
        producer.ProduceAsync(Arg.Any<string>(), Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult<string, byte[]>
            {
                Status = PersistenceStatus.NotPersisted,
                Topic = "topic-orders"
            });

        using var transport = new KafkaMessageTransport(producer: producer);

        var result = await transport.PublishRawAsync("topic-orders", new byte[] { 1 }, TransportMessageMetadata.Create("test"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Kafka.PublishFailed");
        result.Error.Description.Should().Contain("Message was not persisted");
    }

    [Fact]
    public async Task PublishRawAsync_WhenProduceThrows_LogsErrorAndReturnsFailureResult()
    {
        var producer = Substitute.For<IProducer<string, byte[]>>();
        var logger = Substitute.For<ILogger<KafkaMessageTransport>>();
        producer.ProduceAsync(Arg.Any<string>(), Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KafkaException(new Confluent.Kafka.Error(ErrorCode.BrokerNotAvailable, "Broker down")));

        using var transport = new KafkaMessageTransport(producer: producer, logger: logger);

        var result = await transport.PublishRawAsync("topic-orders", new byte[] { 1 }, TransportMessageMetadata.Create("test"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Kafka.ProduceException");
        result.Error.Description.Should().Contain("Broker down");

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Failed to publish message to Kafka topic")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task PublishRawAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var producer = Substitute.For<IProducer<string, byte[]>>();
        var logger = Substitute.For<ILogger<KafkaMessageTransport>>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        producer.ProduceAsync(Arg.Any<string>(), Arg.Any<Message<string, byte[]>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        using var transport = new KafkaMessageTransport(producer: producer, logger: logger);

        Func<Task> act = async () => await transport.PublishRawAsync("topic-1", new byte[] { 1 }, TransportMessageMetadata.Create("test"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        logger.DidNotReceive().Log(LogLevel.Error, Arg.Any<EventId>(), Arg.Any<object>(), Arg.Any<Exception>(), Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region SubscribeAsync

    [Fact]
    public async Task SubscribeAsync_Disposed_ThrowsObjectDisposedException()
    {
        var producer = Substitute.For<IProducer<string, byte[]>>();
        var transport = new KafkaMessageTransport(producer: producer);
        transport.Dispose();

        Func<Task> act = async () => await transport.SubscribeAsync("topic-1", (_, _, _) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SubscribeAsync_InvalidDestination_ThrowsArgumentException(string? destination)
    {
        var producer = Substitute.For<IProducer<string, byte[]>>();
        using var transport = new KafkaMessageTransport(producer: producer);

        Func<Task> act = async () => await transport.SubscribeAsync(destination!, (_, _, _) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SubscribeAsync_NullHandler_ThrowsArgumentNullException()
    {
        var producer = Substitute.For<IProducer<string, byte[]>>();
        using var transport = new KafkaMessageTransport(producer: producer);

        Func<Task> act = async () => await transport.SubscribeAsync("topic-1", null!, new TransportSubscriptionOptions());
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("messageHandler");
    }

    [Fact]
    public async Task SubscribeAsync_NullOptions_ThrowsArgumentNullException()
    {
        var producer = Substitute.For<IProducer<string, byte[]>>();
        using var transport = new KafkaMessageTransport(producer: producer);

        Func<Task> act = async () => await transport.SubscribeAsync("topic-1", (_, _, _) => ValueTask.FromResult(TransportAckResult.Ack), null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task SubscribeAsync_ProcessesMessages_AndCommitsOnAckWhenAutoCommitDisabled()
    {
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        using var cts = new CancellationTokenSource();

        var kafkaHeaders = new Headers
        {
            { "message-id", Encoding.UTF8.GetBytes("msg-sub-1") },
            { "message-type", Encoding.UTF8.GetBytes("OrderCompleted") },
            { "correlation-id", Encoding.UTF8.GetBytes("corr-sub-1") },
            { "causation-id", Encoding.UTF8.GetBytes("cause-sub-1") },
            { "traceparent", Encoding.UTF8.GetBytes("00-trace-kafka-01") },            { "tenant-id", Encoding.UTF8.GetBytes("tenant-k") },
            { "content-type", Encoding.UTF8.GetBytes("application/custom-avro") },
            { "schema-version", Encoding.UTF8.GetBytes("5") },
            { "x-custom-key", Encoding.UTF8.GetBytes("custom-data") }
        };

        var consumeResult = new ConsumeResult<string, byte[]>
        {
            Topic = "orders-topic",
            Message = new Message<string, byte[]>
            {
                Key = "order-pk-1",
                Value = Encoding.UTF8.GetBytes("payload bytes"),
                Headers = kafkaHeaders
            }
        };

        var callCount = 0;
        var tcsCommitted = new TaskCompletionSource<bool>();

        consumer.Consume(Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return consumeResult;
                }

                var token = callInfo.Arg<CancellationToken>();
                token.WaitHandle.WaitOne();
                token.ThrowIfCancellationRequested();
                return null!;
            });

        consumer.When(c => c.Commit(Arg.Any<ConsumeResult<string, byte[]>>()))
            .Do(_ => tcsCommitted.TrySetResult(true));

        var options = Options.Create(new KafkaTransportOptions
        {
            BootstrapServers = "kafka.cluster:9092",
            GroupId = "custom-order-group",
            EnableAutoCommit = false
        });

        ReadOnlyMemory<byte> capturedPayload = default;
        TransportMessageMetadata? capturedMeta = null;
        ConsumerConfig? capturedConsumerConfig = null;

        using var transport = new KafkaMessageTransport(
            options: options,
            consumerFactory: cfg =>
            {
                capturedConsumerConfig = cfg;
                return consumer;
            });

        var subResult = await transport.SubscribeAsync("orders-topic", (p, m, ct) =>
        {
            capturedPayload = p;
            capturedMeta = m;
            return ValueTask.FromResult(TransportAckResult.Ack);
        }, new TransportSubscriptionOptions(), cts.Token);

        await Task.WhenAny(tcsCommitted.Task, Task.Delay(3000));
        cts.Cancel();

        subResult.IsSuccess.Should().BeTrue();
        tcsCommitted.Task.IsCompletedSuccessfully.Should().BeTrue();
        capturedConsumerConfig.Should().NotBeNull();
        capturedConsumerConfig!.BootstrapServers.Should().Be("kafka.cluster:9092");
        capturedConsumerConfig.GroupId.Should().Be("custom-order-group");
        capturedConsumerConfig.EnableAutoCommit.Should().BeFalse();
        capturedConsumerConfig.AutoOffsetReset.Should().Be(AutoOffsetReset.Earliest);

        consumer.Received(1).Subscribe("orders-topic");
        consumer.Received(1).Commit(consumeResult);

        capturedPayload.ToArray().Should().BeEquivalentTo(Encoding.UTF8.GetBytes("payload bytes"));
        capturedMeta.Should().NotBeNull();
        capturedMeta!.MessageId.Should().Be("msg-sub-1");
        capturedMeta.MessageType.Should().Be("OrderCompleted");
        capturedMeta.CorrelationId.Should().Be("corr-sub-1");
        capturedMeta.CausationId.Should().Be("cause-sub-1");
        capturedMeta.TraceParent.Should().Be("00-trace-kafka-01");
        capturedMeta.TenantId.Should().Be("tenant-k");
        capturedMeta.PartitionKey.Should().Be("order-pk-1");
        capturedMeta.ContentType.Should().Be("application/custom-avro");
        capturedMeta.SchemaVersion.Should().Be(5);
        capturedMeta.Headers!["x-custom-key"].Should().Be("custom-data");
    }

    [Fact]
    public async Task SubscribeAsync_WhenAutoCommitEnabled_DoesNotCallCommitOnAck()
    {
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        using var cts = new CancellationTokenSource();

        var consumeResult = new ConsumeResult<string, byte[]>
        {
            Topic = "orders-topic",
            Message = new Message<string, byte[]>
            {
                Key = "k1",
                Value = new byte[] { 1 },
                Headers = new Headers()
            }
        };

        var callCount = 0;
        var tcsHandled = new TaskCompletionSource<bool>();

        consumer.Consume(Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return consumeResult;
                }

                var token = callInfo.Arg<CancellationToken>();
                token.WaitHandle.WaitOne();
                token.ThrowIfCancellationRequested();
                return null;
            });

        var options = Options.Create(new KafkaTransportOptions
        {
            EnableAutoCommit = true
        });

        using var transport = new KafkaMessageTransport(
            options: options,
            consumerFactory: _ => consumer);

        await transport.SubscribeAsync("orders-topic", (p, m, ct) =>
        {
            tcsHandled.TrySetResult(true);
            return ValueTask.FromResult(TransportAckResult.Ack);
        }, new TransportSubscriptionOptions(), cts.Token);

        await Task.WhenAny(tcsHandled.Task, Task.Delay(2000));
        cts.Cancel();

        tcsHandled.Task.IsCompletedSuccessfully.Should().BeTrue();
        consumer.DidNotReceive().Commit(Arg.Any<ConsumeResult<string, byte[]>>());
    }

    [Fact]
    public async Task SubscribeAsync_WhenNack_DoesNotCallCommit()
    {
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        using var cts = new CancellationTokenSource();

        var consumeResult = new ConsumeResult<string, byte[]>
        {
            Topic = "orders-topic",
            Message = new Message<string, byte[]>
            {
                Key = "k1",
                Value = new byte[] { 1 },
                Headers = new Headers()
            }
        };

        var callCount = 0;
        var tcsHandled = new TaskCompletionSource<bool>();

        consumer.Consume(Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return consumeResult;
                }

                var token = callInfo.Arg<CancellationToken>();
                token.WaitHandle.WaitOne();
                token.ThrowIfCancellationRequested();
                return null;
            });

        var options = Options.Create(new KafkaTransportOptions
        {
            EnableAutoCommit = false
        });

        using var transport = new KafkaMessageTransport(
            options: options,
            consumerFactory: _ => consumer);

        await transport.SubscribeAsync("orders-topic", (p, m, ct) =>
        {
            tcsHandled.TrySetResult(true);
            return ValueTask.FromResult(TransportAckResult.NackRequeue);
        }, new TransportSubscriptionOptions(), cts.Token);

        await Task.WhenAny(tcsHandled.Task, Task.Delay(2000));
        cts.Cancel();

        tcsHandled.Task.IsCompletedSuccessfully.Should().BeTrue();
        consumer.DidNotReceive().Commit(Arg.Any<ConsumeResult<string, byte[]>>());
    }

    [Fact]
    public async Task SubscribeAsync_FallbackMetadata_WhenHeadersNullOrEmpty_GeneratesDefaults()
    {
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        using var cts = new CancellationTokenSource();

        var consumeResult = new ConsumeResult<string, byte[]>
        {
            Topic = "fallback-topic",
            Message = new Message<string, byte[]>
            {
                Key = "k-fallback",
                Value = new byte[] { 1, 2, 3 },
                Headers = null
            }
        };

        var callCount = 0;
        var tcsHandled = new TaskCompletionSource<bool>();

        consumer.Consume(Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return consumeResult;
                }

                var token = callInfo.Arg<CancellationToken>();
                token.WaitHandle.WaitOne();
                token.ThrowIfCancellationRequested();
                return null;
            });

        using var transport = new KafkaMessageTransport(consumerFactory: _ => consumer);
        TransportMessageMetadata? capturedMeta = null;

        await transport.SubscribeAsync("fallback-topic", (p, m, ct) =>
        {
            capturedMeta = m;
            tcsHandled.TrySetResult(true);
            return ValueTask.FromResult(TransportAckResult.Ack);
        }, new TransportSubscriptionOptions(), cts.Token);

        await Task.WhenAny(tcsHandled.Task, Task.Delay(2000));
        cts.Cancel();

        tcsHandled.Task.IsCompletedSuccessfully.Should().BeTrue();
        capturedMeta.Should().NotBeNull();
        capturedMeta!.MessageId.Length.Should().Be(32);
        Guid.TryParseExact(capturedMeta.MessageId, "N", out _).Should().BeTrue();
        capturedMeta.CorrelationId.Length.Should().Be(32);
        Guid.TryParseExact(capturedMeta.CorrelationId, "N", out _).Should().BeTrue();
        capturedMeta.MessageType.Should().Be("KafkaMessage");
        capturedMeta.ContentType.Should().Be("application/json");
        capturedMeta.SchemaVersion.Should().Be(1);
        capturedMeta.Headers.Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeAsync_PollingLoopThrowsException_LogsErrorAndClosesConsumer()
    {
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        var logger = Substitute.For<ILogger<KafkaMessageTransport>>();
        using var cts = new CancellationTokenSource();

        var tcsLogged = new TaskCompletionSource<bool>();
        var tcsClosed = new TaskCompletionSource<bool>();

        logger.When(l => l.Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Error processing Kafka stream on topic")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>()))
            .Do(_ => tcsLogged.TrySetResult(true));

        consumer.When(c => c.Close())
            .Do(_ => tcsClosed.TrySetResult(true));

        consumer.Consume(Arg.Any<CancellationToken>())
            .Throws(new KafkaException(new Confluent.Kafka.Error(ErrorCode.Unknown, "Fatal stream error")));

        using var transport = new KafkaMessageTransport(
            consumerFactory: _ => consumer,
            logger: logger);

        await transport.SubscribeAsync("error-topic", (p, m, ct) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions(), cts.Token);

        await Task.WhenAny(Task.WhenAll(tcsLogged.Task, tcsClosed.Task), Task.Delay(3000));
        cts.Cancel();

        tcsLogged.Task.IsCompletedSuccessfully.Should().BeTrue();
        tcsClosed.Task.IsCompletedSuccessfully.Should().BeTrue();
        consumer.Received(1).Close();
    }

    [Fact]
    public async Task SubscribeAsync_WhenOperationCanceledExceptionThrown_ClosesConsumerWithoutLoggingError()
    {
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        var logger = Substitute.For<ILogger<KafkaMessageTransport>>();
        using var cts = new CancellationTokenSource();
        var tcsClosed = new TaskCompletionSource<bool>();

        consumer.When(c => c.Close())
            .Do(_ => tcsClosed.TrySetResult(true));

        consumer.Consume(Arg.Any<CancellationToken>())
            .Throws(new OperationCanceledException());

        using var transport = new KafkaMessageTransport(
            consumerFactory: _ => consumer,
            logger: logger);

        await transport.SubscribeAsync("cancel-topic", (p, m, ct) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions(), cts.Token);

        await Task.WhenAny(tcsClosed.Task, Task.Delay(3000));
        cts.Cancel();

        tcsClosed.Task.IsCompletedSuccessfully.Should().BeTrue();
        consumer.Received(1).Close();
    }

    [Fact]
    public async Task SubscribeAsync_WhenCancelledAfterIteration_ExitsLoopGracefully()
    {
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        using var cts = new CancellationTokenSource();
        var tcsClosed = new TaskCompletionSource<bool>();

        consumer.When(c => c.Close())
            .Do(_ => tcsClosed.TrySetResult(true));

        var consumeResult = new ConsumeResult<string, byte[]>
        {
            Topic = "clean-exit-topic",
            Message = new Message<string, byte[]>
            {
                Key = "k1",
                Value = new byte[] { 1 },
                Headers = new Headers()
            }
        };

        consumer.Consume(Arg.Any<CancellationToken>())
            .Returns(consumeResult);

        using var transport = new KafkaMessageTransport(
            consumerFactory: _ => consumer);

        await transport.SubscribeAsync("clean-exit-topic", (p, m, ct) =>
        {
            cts.Cancel();
            return ValueTask.FromResult(TransportAckResult.Ack);
        }, new TransportSubscriptionOptions(), cts.Token);

        await Task.WhenAny(tcsClosed.Task, Task.Delay(3000));

        tcsClosed.Task.IsCompletedSuccessfully.Should().BeTrue();
        consumer.Received(1).Close();
    }

    #endregion

    #region Dispose & DisposeAsync

    [Fact]
    public async Task Dispose_WithActiveSubscriptions_CancelsAndDisposesAllSubscriptionsAndProducer()
    {
        var producer = Substitute.For<IProducer<string, byte[]>>();
        var consumer = Substitute.For<IConsumer<string, byte[]>>();

        consumer.Consume(Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var token = callInfo.Arg<CancellationToken>();
                token.WaitHandle.WaitOne();
                token.ThrowIfCancellationRequested();
                return null!;
            });

        var transport = new KafkaMessageTransport(producer: producer, consumerFactory: _ => consumer);

        await transport.SubscribeAsync("t1", (p, m, ct) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());
        await transport.SubscribeAsync("t2", (p, m, ct) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());

        transport.Dispose();
        transport.Dispose(); // Idempotent call

        producer.Received(1).Dispose();

        Func<Task> act = async () => await transport.PublishRawAsync("t1", new byte[] { 1 }, TransportMessageMetadata.Create("test"));
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task DisposeAsync_CallsDispose_IsIdempotent()
    {
        var producer = Substitute.For<IProducer<string, byte[]>>();
        var transport = new KafkaMessageTransport(producer: producer);

        await transport.DisposeAsync();
        await transport.DisposeAsync();

        producer.Received(1).Dispose();
    }

    [Fact]
    public async Task SubscribeAsync_WithMaxConcurrencyGreaterThanOne_ProcessesMessagesConcurrently()
    {
        var consumer = Substitute.For<IConsumer<string, byte[]>>();
        var transport = new KafkaMessageTransport(consumerFactory: _ => consumer);

        var message1 = new Message<string, byte[]> { Key = "k1", Value = new byte[] { 1 } };
        var message2 = new Message<string, byte[]> { Key = "k2", Value = new byte[] { 2 } };
        var cr1 = new ConsumeResult<string, byte[]> { Message = message1, Topic = "topic", Partition = 0, Offset = 1 };
        var cr2 = new ConsumeResult<string, byte[]> { Message = message2, Topic = "topic", Partition = 0, Offset = 2 };

        using var cts = new CancellationTokenSource();
        int callCount = 0;

        consumer.Consume(Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                int count = Interlocked.Increment(ref callCount);
                if (count == 1) return cr1;
                if (count == 2) return cr2;

                var ct = callInfo.Arg<CancellationToken>();
                ct.WaitHandle.WaitOne();
                throw new OperationCanceledException();
            });

        int inFlight = 0;
        int maxInFlight = 0;
        var barrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int completedCount = 0;

        var subOptions = new TransportSubscriptionOptions { MaxConcurrency = 2 };

        await transport.SubscribeAsync("topic", async (p, m, ct) =>
        {
            int current = Interlocked.Increment(ref inFlight);
            lock (barrier)
            {
                if (current > maxInFlight) maxInFlight = current;
            }

            if (current >= 2)
            {
                barrier.TrySetResult(true);
            }

            await Task.WhenAny(barrier.Task, Task.Delay(500, CancellationToken.None));

            Interlocked.Decrement(ref inFlight);
            if (Interlocked.Increment(ref completedCount) == 2)
            {
                allDone.TrySetResult(true);
            }

            return TransportAckResult.Ack;
        }, subOptions, cts.Token);

        await allDone.Task;
        cts.Cancel();
        maxInFlight.Should().BeGreaterThan(1, "Kafka messages must be processed concurrently when MaxConcurrency > 1");
        completedCount.Should().Be(2);
    }

    #endregion
}

