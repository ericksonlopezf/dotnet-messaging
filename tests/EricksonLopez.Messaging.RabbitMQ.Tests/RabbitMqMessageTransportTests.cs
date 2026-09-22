// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Messaging.Transport.RabbitMQ;
using EricksonLopez.Result;
using global::RabbitMQ.Client;
using global::RabbitMQ.Client.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace EricksonLopez.Messaging.RabbitMQ.Tests;

[Trait("Category", "Transport")]
public class RabbitMqMessageTransportTests
{
    #region Constructor

    [Fact]
    public void Constructor_DefaultParameters_InitializesCorrectly()
    {
        using var transport = new RabbitMqMessageTransport();
        transport.Should().NotBeNull();
        transport.Should().BeAssignableTo<IAsyncDisposable>();
    }

    [Fact]
    public void Constructor_WithOptions_InitializesCorrectly()
    {
        var options = Options.Create(new RabbitMqTransportOptions
        {
            HostName = "rabbit.prod.internal",
            Port = 5673,
            VirtualHost = "/vhost1",
            UserName = "admin",
            Password = "secret-password",
            ExchangeName = "prod.exchange"
        });

        using var transport = new RabbitMqMessageTransport(options: options);
        transport.Should().NotBeNull();

        var field = typeof(RabbitMqMessageTransport).GetField("_connectionFactory", BindingFlags.NonPublic | BindingFlags.Instance);
        var cf = field?.GetValue(transport) as ConnectionFactory;
        cf.Should().NotBeNull();
        cf!.HostName.Should().Be("rabbit.prod.internal");
        cf.Port.Should().Be(5673);
        cf.VirtualHost.Should().Be("/vhost1");
        cf.UserName.Should().Be("admin");
        cf.Password.Should().Be("secret-password");
    }

    [Fact]
    public async Task PublishRawAsync_WhenChannelAlreadyOpen_ReturnsImmediatelyWithoutReacquiringInitLock()
    {
        var factory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        using var transport = new RabbitMqMessageTransport(connectionFactory: factory);
        var res1 = await transport.PublishRawAsync("dest", new byte[] { 1 }, TransportMessageMetadata.Create("t"));
        res1.IsSuccess.Should().BeTrue();

        var lockField = typeof(RabbitMqMessageTransport).GetField("_initLock", BindingFlags.NonPublic | BindingFlags.Instance);
        var initLock = (SemaphoreSlim)lockField!.GetValue(transport)!;
        await initLock.WaitAsync();

        try
        {
            var res2 = await transport.PublishRawAsync("dest", new byte[] { 2 }, TransportMessageMetadata.Create("t"));
            res2.IsSuccess.Should().BeTrue();
        }
        finally
        {
            initLock.Release();
        }
    }

    [Fact]
    public void Dispose_DisposesLocksAndResources()
    {
        var transport = new RabbitMqMessageTransport();
        var initLockField = typeof(RabbitMqMessageTransport).GetField("_initLock", BindingFlags.NonPublic | BindingFlags.Instance);
        var publishLockField = typeof(RabbitMqMessageTransport).GetField("_publishLock", BindingFlags.NonPublic | BindingFlags.Instance);
        var initLock = (SemaphoreSlim)initLockField!.GetValue(transport)!;
        var publishLock = (SemaphoreSlim)publishLockField!.GetValue(transport)!;

        transport.Dispose();

        Action checkInit = () => _ = initLock.AvailableWaitHandle;
        Action checkPublish = () => _ = publishLock.AvailableWaitHandle;

        checkInit.Should().Throw<ObjectDisposedException>();
        checkPublish.Should().Throw<ObjectDisposedException>();

        transport.Dispose();
    }

    [Fact]
    public async Task DisposeAsync_DisposesLocksAndResources()
    {
        var transport = new RabbitMqMessageTransport();
        var initLockField = typeof(RabbitMqMessageTransport).GetField("_initLock", BindingFlags.NonPublic | BindingFlags.Instance);
        var publishLockField = typeof(RabbitMqMessageTransport).GetField("_publishLock", BindingFlags.NonPublic | BindingFlags.Instance);
        var initLock = (SemaphoreSlim)initLockField!.GetValue(transport)!;
        var publishLock = (SemaphoreSlim)publishLockField!.GetValue(transport)!;

        await transport.DisposeAsync();

        Action checkInit = () => _ = initLock.AvailableWaitHandle;
        Action checkPublish = () => _ = publishLock.AvailableWaitHandle;

        checkInit.Should().Throw<ObjectDisposedException>();
        checkPublish.Should().Throw<ObjectDisposedException>();

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task Constructor_WithExplicitConnectionFactoryAndLogger_UsesProvidedInstances()
    {
        var factory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        var logger = Substitute.For<ILogger<RabbitMqMessageTransport>>();

        using var transport = new RabbitMqMessageTransport(connectionFactory: factory, logger: logger);
        var result = await transport.PublishRawAsync("test.queue", new byte[] { 1 }, TransportMessageMetadata.Create("test"));

        result.IsSuccess.Should().BeTrue();
        await factory.Received(1).CreateConnectionAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region PublishRawAsync

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PublishRawAsync_InvalidDestination_ThrowsArgumentException(string? invalidDest)
    {
        var factory = Substitute.For<IConnectionFactory>();
        using var transport = new RabbitMqMessageTransport(connectionFactory: factory);

        Func<Task> act = async () => await transport.PublishRawAsync(
            invalidDest!,
            new byte[] { 1, 2 },
            TransportMessageMetadata.Create("test"));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PublishRawAsync_NullMetadata_ThrowsArgumentNullException()
    {
        var factory = Substitute.For<IConnectionFactory>();
        using var transport = new RabbitMqMessageTransport(connectionFactory: factory);

        Func<Task> act = async () => await transport.PublishRawAsync(
            "dest",
            new byte[] { 1, 2 },
            null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishRawAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        var factory = Substitute.For<IConnectionFactory>();
        var transport = new RabbitMqMessageTransport(connectionFactory: factory);
        await transport.DisposeAsync();

        Func<Task> act = async () => await transport.PublishRawAsync(
            "dest",
            new byte[] { 1, 2 },
            TransportMessageMetadata.Create("test"));

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task PublishRawAsync_SuccessWithAllHeadersAndMetadata_PublishesCorrectly()
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();

        channel.IsOpen.Returns(true);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        connectionFactory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        var options = Options.Create(new RabbitMqTransportOptions
        {
            ExchangeName = "amq.direct"
        });

        using var transport = new RabbitMqMessageTransport(options, connectionFactory);

        var payload = Encoding.UTF8.GetBytes("payload");
        var metadata = new TransportMessageMetadata(
            MessageId: "MSG-RMQ-1",
            MessageType: "OrderCreated",
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: "CORR-RMQ-1",
            CausationId: "CAUSE-1",
            TraceParent: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            TenantId: "TENANT-1",
            PartitionKey: "PK-1",
            ContentType: "application/custom+json",
            SchemaVersion: 2,
            Headers: new Dictionary<string, string> { ["customHeader"] = "customValue" });

        // Act - First publish
        var result1 = await transport.PublishRawAsync("orders.created", payload, metadata, CancellationToken.None);

        // Act - Second publish (reuses existing open channel and connection)
        var result2 = await transport.PublishRawAsync("orders.created", payload, metadata, CancellationToken.None);

        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();

        await connectionFactory.Received(1).CreateConnectionAsync(Arg.Any<CancellationToken>());
        await connection.Received(1).CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>());

        await channel.Received(2).BasicPublishAsync(
            exchange: "amq.direct",
            routingKey: "orders.created",
            mandatory: false,
            basicProperties: Arg.Is<BasicProperties>(p =>
                p.MessageId == "MSG-RMQ-1" &&
                p.CorrelationId == "CORR-RMQ-1" &&
                p.Type == "OrderCreated" &&
                p.ContentType == "application/custom+json" &&
                p.Headers != null &&
                (string)p.Headers["traceparent"]! == "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01" &&
                (string)p.Headers["causation-id"]! == "CAUSE-1" &&
                (string)p.Headers["tenant-id"]! == "TENANT-1" &&
                (string)p.Headers["partition-key"]! == "PK-1" &&
                (string)p.Headers["schema-version"]! == "2" &&
                (string)p.Headers["customHeader"]! == "customValue"),
            body: Arg.Any<ReadOnlyMemory<byte>>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_WhenPublishChannelAlreadyOpen_BypassesInitLockFastPath()
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();

        channel.IsOpen.Returns(true);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        connectionFactory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        using var transport = new RabbitMqMessageTransport(connectionFactory: connectionFactory);

        // First publish initializes _publishChannel
        var res1 = await transport.PublishRawAsync("orders.created", new byte[] { 1 }, TransportMessageMetadata.Create("test"));
        res1.IsSuccess.Should().BeTrue();

        // Second publish succeeds immediately reusing the already open channel without creating a new channel
        var res2 = await transport.PublishRawAsync("orders.created", new byte[] { 2 }, TransportMessageMetadata.Create("test"));
        res2.IsSuccess.Should().BeTrue();

        await connection.Received(1).CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_ThenSubscribeAsync_SharesSingleConnectionAndReleasesInitLock()
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var publishChannel = Substitute.For<IChannel>();
        var consumerChannel1 = Substitute.For<IChannel>();
        var consumerChannel2 = Substitute.For<IChannel>();

        publishChannel.IsOpen.Returns(true);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(publishChannel), Task.FromResult(consumerChannel1), Task.FromResult(consumerChannel2));
        connectionFactory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        using var transport = new RabbitMqMessageTransport(connectionFactory: connectionFactory);

        // 1. Publish creates connection and publishChannel
        var pubRes = await transport.PublishRawAsync("topic", new byte[] { 1 }, TransportMessageMetadata.Create("t"));
        pubRes.IsSuccess.Should().BeTrue();

        // 2. Subscribe reuses existing connection and releases lock properly
        var subRes1 = await transport.SubscribeAsync("queue1", (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());
        subRes1.IsSuccess.Should().BeTrue();

        // 3. Second Subscribe verifies lock was released in previous call and connection was shared
        var subRes2 = await transport.SubscribeAsync("queue2", (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());
        subRes2.IsSuccess.Should().BeTrue();

        await connectionFactory.Received(1).CreateConnectionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_WhenChannelIsClosed_RecreatesPublishChannelAndSharesConnection()
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel1 = Substitute.For<IChannel>();
        var channel2 = Substitute.For<IChannel>();

        channel1.IsOpen.Returns(true, false); // Open on first call, closed on second
        channel2.IsOpen.Returns(true);

        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel1), Task.FromResult(channel2));
        connectionFactory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        using var transport = new RabbitMqMessageTransport(connectionFactory: connectionFactory);

        var payload = new byte[] { 1 };
        var meta = TransportMessageMetadata.Create("test");

        var res1 = await transport.PublishRawAsync("dest", payload, meta);
        channel1.IsOpen.Returns(false); // Now channel1 is closed
        var res2 = await transport.PublishRawAsync("dest", payload, meta);

        res1.IsSuccess.Should().BeTrue();
        res2.IsSuccess.Should().BeTrue();

        await connection.Received(2).CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>());
        await connectionFactory.Received(1).CreateConnectionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_ConcurrentInitialization_CreatesChannelOnlyOnce()
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();

        channel.IsOpen.Returns(true);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(10);
                return channel;
            });
        connectionFactory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        using var transport = new RabbitMqMessageTransport(connectionFactory: connectionFactory);

        var tasks = Enumerable.Range(0, 10).Select(_ =>
            transport.PublishRawAsync("topic", new byte[] { 1 }, TransportMessageMetadata.Create("t")).AsTask()
        ).ToArray();

        var results = await Task.WhenAll(tasks);
        results.All(r => r.IsSuccess).Should().BeTrue();

        await connection.Received(1).CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_MinimalMetadataWithExplicitNullContentType_FallsBackToApplicationJson()
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();

        channel.IsOpen.Returns(true);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        connectionFactory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        using var transport = new RabbitMqMessageTransport(connectionFactory: connectionFactory);

        var payload = Encoding.UTF8.GetBytes("payload");
        var metadata = new TransportMessageMetadata(
            MessageId: "MSG-MIN-1",
            MessageType: "SimpleMsg",
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: "CORR-MIN-1",
            CausationId: null,
            TraceParent: null,
            TenantId: null,
            PartitionKey: null,
            ContentType: null!,
            SchemaVersion: 1,
            Headers: null);

        var result = await transport.PublishRawAsync("dest", payload, metadata);

        result.IsSuccess.Should().BeTrue();
        await channel.Received(1).BasicPublishAsync(
            exchange: string.Empty,
            routingKey: "dest",
            mandatory: false,
            basicProperties: Arg.Is<BasicProperties>(p =>
                p.MessageId == "MSG-MIN-1" &&
                p.CorrelationId == "CORR-MIN-1" &&
                p.Type == "SimpleMsg" &&
                p.ContentType == "application/json" &&
                p.Headers == null),
            body: Arg.Any<ReadOnlyMemory<byte>>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_ChannelThrowsException_LogsErrorAndReturnsFailure()
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();
        var logger = Substitute.For<ILogger<RabbitMqMessageTransport>>();

        channel.IsOpen.Returns(true);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        connectionFactory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        channel.BasicPublishAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<BasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Broker disconnected"));

        using var transport = new RabbitMqMessageTransport(connectionFactory: connectionFactory, logger: logger);

        var result = await transport.PublishRawAsync("orders.created", new byte[] { 1 }, TransportMessageMetadata.Create("OrderCreated"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("RabbitMQ.PublishFailed");
        result.Error.Description.Should().Contain("Broker disconnected");

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Failed to publish message to RabbitMQ destination")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task PublishRawAsync_WhenCancellationRequested_ThrowsOperationCanceledException()
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();
        var logger = Substitute.For<ILogger<RabbitMqMessageTransport>>();

        channel.IsOpen.Returns(true);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        connectionFactory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        channel.BasicPublishAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<BasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        using var transport = new RabbitMqMessageTransport(connectionFactory: connectionFactory, logger: logger);

        Func<Task> act = async () => await transport.PublishRawAsync("orders.created", new byte[] { 1 }, TransportMessageMetadata.Create("test"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        logger.DidNotReceive().Log(LogLevel.Error, Arg.Any<EventId>(), Arg.Any<object>(), Arg.Any<Exception>(), Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region SubscribeAsync

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SubscribeAsync_InvalidDestination_ThrowsArgumentException(string? invalidDest)
    {
        var factory = Substitute.For<IConnectionFactory>();
        using var transport = new RabbitMqMessageTransport(connectionFactory: factory);

        Func<Task> act = async () => await transport.SubscribeAsync(
            invalidDest!,
            (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            new TransportSubscriptionOptions());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SubscribeAsync_NullHandler_ThrowsArgumentNullException()
    {
        var factory = Substitute.For<IConnectionFactory>();
        using var transport = new RabbitMqMessageTransport(connectionFactory: factory);

        Func<Task> act = async () => await transport.SubscribeAsync(
            "queue",
            null!,
            new TransportSubscriptionOptions());

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SubscribeAsync_NullOptions_ThrowsArgumentNullException()
    {
        var factory = Substitute.For<IConnectionFactory>();
        using var transport = new RabbitMqMessageTransport(connectionFactory: factory);

        Func<Task> act = async () => await transport.SubscribeAsync(
            "queue",
            (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SubscribeAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        var factory = Substitute.For<IConnectionFactory>();
        var transport = new RabbitMqMessageTransport(connectionFactory: factory);
        await transport.DisposeAsync();

        Func<Task> act = async () => await transport.SubscribeAsync(
            "queue",
            (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            new TransportSubscriptionOptions());

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task SubscribeAsync_ConnectionThrowsException_LogsErrorAndReturnsFailure()
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        var logger = Substitute.For<ILogger<RabbitMqMessageTransport>>();

        connectionFactory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Socket refused"));

        using var transport = new RabbitMqMessageTransport(connectionFactory: connectionFactory, logger: logger);

        var result = await transport.SubscribeAsync(
            "bad.queue",
            (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            new TransportSubscriptionOptions());

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("RabbitMQ.SubscribeFailed");
        result.Error.Description.Should().Contain("Socket refused");

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Failed to subscribe to RabbitMQ queue")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task SubscribeAsync_WhenCancellationRequested_ThrowsOperationCanceledException()
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        var logger = Substitute.For<ILogger<RabbitMqMessageTransport>>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        connectionFactory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        using var transport = new RabbitMqMessageTransport(connectionFactory: connectionFactory, logger: logger);

        Func<Task> act = async () => await transport.SubscribeAsync(
            "cancel.queue",
            (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            new TransportSubscriptionOptions(),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        logger.DidNotReceive().Log(LogLevel.Error, Arg.Any<EventId>(), Arg.Any<object>(), Arg.Any<Exception>(), Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task SubscribeAsync_ConsumerReceivedEvents_HandlesAckNackAndDeadLetter()
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();

        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        connectionFactory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        IAsyncBasicConsumer? registeredConsumer = null;
        await channel.BasicConsumeAsync(
            queue: Arg.Any<string>(),
            autoAck: Arg.Any<bool>(),
            consumerTag: Arg.Any<string>(),
            noLocal: Arg.Any<bool>(),
            exclusive: Arg.Any<bool>(),
            arguments: Arg.Any<IDictionary<string, object?>>(),
            consumer: Arg.Do<IAsyncBasicConsumer>(c => registeredConsumer = c),
            cancellationToken: Arg.Any<CancellationToken>());

        var ackResultToReturn = TransportAckResult.Ack;
        TransportMessageMetadata? capturedMetadata = null;

        using var transport = new RabbitMqMessageTransport(connectionFactory: connectionFactory);

        var subResult = await transport.SubscribeAsync(
            "orders.queue",
            (body, meta, ct) =>
            {
                capturedMetadata = meta;
                return ValueTask.FromResult(ackResultToReturn);
            },
            new TransportSubscriptionOptions { PrefetchCount = 15, MaxConcurrency = 2 });

        subResult.IsSuccess.Should().BeTrue();
        registeredConsumer.Should().NotBeNull();

        await channel.Received(1).BasicQosAsync(prefetchSize: 0, prefetchCount: 15, global: false, cancellationToken: Arg.Any<CancellationToken>());
        await channel.Received(1).BasicConsumeAsync(
            queue: "orders.queue",
            autoAck: false,
            consumerTag: string.Empty,
            noLocal: false,
            exclusive: false,
            arguments: null,
            consumer: Arg.Any<IAsyncBasicConsumer>(),
            cancellationToken: Arg.Any<CancellationToken>());

        var asyncConsumer = (IAsyncBasicConsumer)registeredConsumer!;

        // 1. Test ReceivedAsync with Ack, byte[] headers, string headers, custom contentType
        var props = new BasicProperties
        {
            MessageId = "MSG-1",
            CorrelationId = "CORR-1",
            Type = "OrderCreated",
            ContentType = "application/custom-json",
            Headers = new Dictionary<string, object?>
            {
                ["bytesHeader"] = Encoding.UTF8.GetBytes("bytesValue"),
                ["strHeader"] = "stringValue",
                ["intHeader"] = 42,
                ["customObj"] = new CustomHeaderObj("my-custom-value"),
                ["nullToString"] = new NullToStringObj(),
                ["nullHeader"] = null,
                ["traceparent"] = "00-trace-123-01",
                ["causation-id"] = "CAUSE-10",
                ["tenant-id"] = "TENANT-10",
                ["partition-key"] = "PK-10",
                ["schema-version"] = "4"
            }
        };

        var ea1 = new BasicDeliverEventArgs(
            consumerTag: "tag1",
            deliveryTag: 101UL,
            redelivered: false,
            exchange: "ex",
            routingKey: "rk",
            properties: props,
            body: new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("payload1")));

        await asyncConsumer.HandleBasicDeliverAsync(ea1.ConsumerTag, ea1.DeliveryTag, ea1.Redelivered, ea1.Exchange, ea1.RoutingKey, ea1.BasicProperties, ea1.Body, CancellationToken.None);

        capturedMetadata.Should().NotBeNull();
        capturedMetadata!.MessageId.Should().Be("MSG-1");
        capturedMetadata.CorrelationId.Should().Be("CORR-1");
        capturedMetadata.MessageType.Should().Be("OrderCreated");
        capturedMetadata.ContentType.Should().Be("application/custom-json");
        capturedMetadata.TraceParent.Should().Be("00-trace-123-01");
        capturedMetadata.CausationId.Should().Be("CAUSE-10");
        capturedMetadata.TenantId.Should().Be("TENANT-10");
        capturedMetadata.PartitionKey.Should().Be("PK-10");
        capturedMetadata.SchemaVersion.Should().Be(4);
        capturedMetadata.Headers!["bytesHeader"].Should().Be("bytesValue");
        capturedMetadata.Headers!["strHeader"].Should().Be("stringValue");
        capturedMetadata.Headers!["intHeader"].Should().Be("42");
        capturedMetadata.Headers!["customObj"].Should().Be("my-custom-value");
        capturedMetadata.Headers!["nullToString"].Should().BeEmpty();
        await channel.Received(1).BasicAckAsync(101UL, false, Arg.Any<CancellationToken>());

        // 2. Test ReceivedAsync with NackRequeue
        ackResultToReturn = TransportAckResult.NackRequeue;
        var ea2 = new BasicDeliverEventArgs(
            consumerTag: "tag2",
            deliveryTag: 102UL,
            redelivered: false,
            exchange: "ex",
            routingKey: "rk",
            properties: props,
            body: new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("payload2")));

        await asyncConsumer.HandleBasicDeliverAsync(ea2.ConsumerTag, ea2.DeliveryTag, ea2.Redelivered, ea2.Exchange, ea2.RoutingKey, ea2.BasicProperties, ea2.Body, CancellationToken.None);
        await channel.Received(1).BasicNackAsync(102UL, false, true, Arg.Any<CancellationToken>());

        // 3. Test ReceivedAsync with DeadLetter
        ackResultToReturn = TransportAckResult.DeadLetter;
        var ea3 = new BasicDeliverEventArgs(
            consumerTag: "tag3",
            deliveryTag: 103UL,
            redelivered: false,
            exchange: "ex",
            routingKey: "rk",
            properties: props,
            body: new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("payload3")));

        await asyncConsumer.HandleBasicDeliverAsync(ea3.ConsumerTag, ea3.DeliveryTag, ea3.Redelivered, ea3.Exchange, ea3.RoutingKey, ea3.BasicProperties, ea3.Body, CancellationToken.None);
        await channel.Received(1).BasicNackAsync(103UL, false, false, Arg.Any<CancellationToken>());

        // 4. Test ReceivedAsync with invalid schema version string -> fallback to 1
        var invalidVerProps = new BasicProperties
        {
            Headers = new Dictionary<string, object?> { ["schema-version"] = "not-an-int" }
        };
        var ea4 = new BasicDeliverEventArgs(
            consumerTag: "tag4",
            deliveryTag: 104UL,
            redelivered: false,
            exchange: "ex",
            routingKey: "rk",
            properties: invalidVerProps,
            body: new ReadOnlyMemory<byte>(Array.Empty<byte>()));

        await asyncConsumer.HandleBasicDeliverAsync(ea4.ConsumerTag, ea4.DeliveryTag, ea4.Redelivered, ea4.Exchange, ea4.RoutingKey, ea4.BasicProperties, ea4.Body, CancellationToken.None);
        capturedMetadata.Should().NotBeNull();
        capturedMetadata!.SchemaVersion.Should().Be(1);
        capturedMetadata.MessageId.Length.Should().Be(32);
        Guid.TryParseExact(capturedMetadata.MessageId, "N", out _).Should().BeTrue();
        capturedMetadata.CorrelationId.Length.Should().Be(32);
        Guid.TryParseExact(capturedMetadata.CorrelationId, "N", out _).Should().BeTrue();
        capturedMetadata.MessageType.Should().Be("application/octet-stream");
        capturedMetadata.ContentType.Should().Be("application/json");

        // 5. Test ReceivedAsync with empty properties
        var emptyProps = new BasicProperties();
        var ea5 = new BasicDeliverEventArgs(
            consumerTag: "tag5",
            deliveryTag: 105UL,
            redelivered: false,
            exchange: "ex",
            routingKey: "rk",
            properties: emptyProps,
            body: new ReadOnlyMemory<byte>(Array.Empty<byte>()));

        await asyncConsumer.HandleBasicDeliverAsync(ea5.ConsumerTag, ea5.DeliveryTag, ea5.Redelivered, ea5.Exchange, ea5.RoutingKey, ea5.BasicProperties, ea5.Body, CancellationToken.None);
        capturedMetadata.Should().NotBeNull();
        capturedMetadata!.Headers.Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeAsync_PrefetchCountZeroOrNegative_DoesNotCallBasicQos()
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();

        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        connectionFactory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        using var transport = new RabbitMqMessageTransport(connectionFactory: connectionFactory);

        await transport.SubscribeAsync(
            "q",
            (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            new TransportSubscriptionOptions { PrefetchCount = 0 });

        await channel.DidNotReceive().BasicQosAsync(
            Arg.Any<uint>(),
            Arg.Any<ushort>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Dispose & DisposeAsync

    [Fact]
    public async Task DisposeAsync_WithOpenChannelAndConnection_ClosesAndDisposesChannelAndConnection()
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();

        channel.IsOpen.Returns(true);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        connectionFactory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        var transport = new RabbitMqMessageTransport(connectionFactory: connectionFactory);
        await transport.PublishRawAsync("topic", new byte[] { 1 }, TransportMessageMetadata.Create("t"));

        await transport.DisposeAsync();
        await transport.DisposeAsync(); // Idempotent second call

        await channel.Received(1).CloseAsync();
        channel.Received(1).Dispose();
        await connection.Received(1).CloseAsync();
        connection.Received(1).Dispose();

        Func<Task> act = async () => await transport.PublishRawAsync("topic", new byte[] { 1 }, TransportMessageMetadata.Create("t"));
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Dispose_Synchronous_CleansUpChannelAndConnection()
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();

        channel.IsOpen.Returns(true);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        connectionFactory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        var transport = new RabbitMqMessageTransport(connectionFactory: connectionFactory);
        await transport.PublishRawAsync("topic", new byte[] { 1 }, TransportMessageMetadata.Create("t"));

        transport.Dispose();
        transport.Dispose(); // Idempotent second call

        channel.Received(1).Dispose();
        connection.Received(1).Dispose();

        Func<Task> act = async () => await transport.PublishRawAsync("topic", new byte[] { 1 }, TransportMessageMetadata.Create("t"));
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Dispose_ThenDisposeAsync_IsIdempotent()
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();

        channel.IsOpen.Returns(true);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        connectionFactory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        var transport = new RabbitMqMessageTransport(connectionFactory: connectionFactory);
        await transport.PublishRawAsync("topic", new byte[] { 1 }, TransportMessageMetadata.Create("t"));

        transport.Dispose();
        await transport.DisposeAsync();

        channel.Received(1).Dispose();
        connection.Received(1).Dispose();
    }

    [Fact]
    public async Task DisposeAsync_ThenDispose_IsIdempotent()
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();

        channel.IsOpen.Returns(true);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        connectionFactory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        var transport = new RabbitMqMessageTransport(connectionFactory: connectionFactory);
        await transport.PublishRawAsync("topic", new byte[] { 1 }, TransportMessageMetadata.Create("t"));

        await transport.DisposeAsync();
        transport.Dispose();

        channel.Received(1).Dispose();
        connection.Received(1).Dispose();
    }

    [Fact]
    public async Task PublishRawAsync_ConcurrentPublishCalls_AreSynchronized()
    {
        var connectionFactory = Substitute.For<IConnectionFactory>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();

        channel.IsOpen.Returns(true);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        connectionFactory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        int concurrentInvocations = 0;
        int maxConcurrencyObserved = 0;

        channel.When(c => c.BasicPublishAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<BasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>()))
            .Do(async _ =>
            {
                var current = Interlocked.Increment(ref concurrentInvocations);
                lock (connection)
                {
                    if (current > maxConcurrencyObserved)
                    {
                        maxConcurrencyObserved = current;
                    }
                }
                await Task.Delay(10);
                Interlocked.Decrement(ref concurrentInvocations);
            });

        using var transport = new RabbitMqMessageTransport(connectionFactory: connectionFactory);

        var tasks = Enumerable.Range(0, 10).Select(_ =>
            transport.PublishRawAsync("topic", new byte[] { 1 }, TransportMessageMetadata.Create("t")).AsTask()
        ).ToArray();

        var results = await Task.WhenAll(tasks);
        results.All(r => r.IsSuccess).Should().BeTrue();

        maxConcurrencyObserved.Should().Be(1);
    }

    #endregion

    private sealed class CustomHeaderObj(string val)
    {
        public override string ToString() => val;
    }

    private sealed class NullToStringObj
    {
        public override string? ToString() => null;
    }
}






