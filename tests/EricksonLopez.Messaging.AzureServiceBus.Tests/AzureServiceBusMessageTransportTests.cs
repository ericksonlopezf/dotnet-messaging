// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Azure.Messaging.ServiceBus;
using EricksonLopez.Messaging.AzureServiceBus.Tests.Common;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Messaging.Transport.AzureServiceBus;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace EricksonLopez.Messaging.AzureServiceBus.Tests;

[Trait("Category", "Transport")]
public class AzureServiceBusMessageTransportTests
{
    #region Options & DI

    [Fact]
    public void AzureServiceBusTransportOptions_DefaultsAndSetters_WorkCorrectly()
    {
        var options = new AzureServiceBusTransportOptions();

        options.ConnectionString.Should().BeEmpty();
        options.FullyQualifiedNamespace.Should().BeEmpty();
        options.Credential.Should().BeNull();

        options.ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=secret";
        options.FullyQualifiedNamespace = "test.servicebus.windows.net";
        var credential = Substitute.For<global::Azure.Core.TokenCredential>();
        options.Credential = credential;

        options.ConnectionString.Should().Be("Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=secret");
        options.FullyQualifiedNamespace.Should().Be("test.servicebus.windows.net");
        options.Credential.Should().BeSameAs(credential);
    }

    [Fact]
    public void AddAzureServiceBusMessagingTransport_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection? services = null;
        Action act = () => services!.AddAzureServiceBusMessagingTransport();
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services")
            .WithMessage("*Service collection cannot be null.*");
    }

    [Fact]
    public void AddAzureServiceBusMessagingTransport_WithoutConfigure_RegistersSingletonTransport()
    {
        var services = new ServiceCollection();
        var client = Substitute.For<ServiceBusClient>();
        services.AddSingleton(client);
        var resultServices = services.AddAzureServiceBusMessagingTransport();

        resultServices.Should().BeSameAs(services);
        using var provider = services.BuildServiceProvider();
        var transport = provider.GetService<IMessageTransport>();
        transport.Should().NotBeNull();
        transport.Should().BeOfType<AzureServiceBusMessageTransport>();
    }

    [Fact]
    public void AddAzureServiceBusMessagingTransport_WithConfigure_RegistersOptionsAndSingleton()
    {
        var services = new ServiceCollection();
        var client = Substitute.For<ServiceBusClient>();
        services.AddSingleton(client);
        bool configured = false;
        services.AddAzureServiceBusMessagingTransport(cfg =>
        {
            configured = true;
            cfg.ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=secret";
        });

        configured.Should().BeFalse(); // Action is invoked on options resolution
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AzureServiceBusTransportOptions>>().Value;
        configured.Should().BeTrue();
        options.ConnectionString.Should().Contain("Endpoint=sb://");

        var transport = provider.GetService<IMessageTransport>();
        transport.Should().NotBeNull();
        transport.Should().BeOfType<AzureServiceBusMessageTransport>();
    }

    #endregion

    #region Constructor & Client Resolution

    [Fact]
    public void Constructor_NoClientNoConnectionStringNoNamespace_ThrowsInvalidOperationException()
    {
        Action act = () => new AzureServiceBusMessageTransport();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("AzureServiceBus connection string, fully-qualified namespace, or explicit client must be provided.");
    }

    [Fact]
    public void Constructor_WithConnectionString_InitializesCorrectly()
    {
        var options = Options.Create(new AzureServiceBusTransportOptions
        {
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=secret"
        });
        var logger = Substitute.For<ILogger<AzureServiceBusMessageTransport>>();

        using var transport = new AzureServiceBusMessageTransport(options, logger: logger);
        transport.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithFullyQualifiedNamespaceAndExplicitCredential_InitializesCorrectly()
    {
        var credential = Substitute.For<global::Azure.Core.TokenCredential>();
        var options = Options.Create(new AzureServiceBusTransportOptions
        {
            FullyQualifiedNamespace = "test.servicebus.windows.net",
            Credential = credential
        });

        using var transport = new AzureServiceBusMessageTransport(options: options);
        transport.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithFullyQualifiedNamespaceAndDefaultCredential_InitializesCorrectly()
    {
        var options = Options.Create(new AzureServiceBusTransportOptions
        {
            FullyQualifiedNamespace = "test.servicebus.windows.net"
        });

        using var transport = new AzureServiceBusMessageTransport(options: options);
        transport.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithExplicitClient_UsesExplicitClient()
    {
        var client = Substitute.For<ServiceBusClient>();
        using var transport = new AzureServiceBusMessageTransport(client: client);
        transport.Should().NotBeNull();
        transport.Should().BeAssignableTo<IAsyncDisposable>();
    }

    #endregion

    #region PublishRawAsync

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PublishRawAsync_InvalidDestination_ThrowsArgumentException(string? invalidDest)
    {
        var client = Substitute.For<ServiceBusClient>();
        using var transport = new AzureServiceBusMessageTransport(client: client);

        Func<Task> act = async () => await transport.PublishRawAsync(
            invalidDest!,
            new byte[] { 1, 2 },
            TransportMessageMetadata.Create("test"));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PublishRawAsync_NullMetadata_ThrowsArgumentNullException()
    {
        var client = Substitute.For<ServiceBusClient>();
        using var transport = new AzureServiceBusMessageTransport(client: client);

        Func<Task> act = async () => await transport.PublishRawAsync(
            "dest",
            new byte[] { 1, 2 },
            null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishRawAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        var client = Substitute.For<ServiceBusClient>();
        var transport = new AzureServiceBusMessageTransport(client: client);
        await transport.DisposeAsync();

        Func<Task> act = async () => await transport.PublishRawAsync(
            "dest",
            new byte[] { 1, 2 },
            TransportMessageMetadata.Create("test"));

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task PublishRawAsync_SuccessWithAllMetadataProperties_SendsMessage()
    {
        var client = Substitute.For<ServiceBusClient>();
        var sender = Substitute.For<ServiceBusSender>();
        client.CreateSender("my-topic").Returns(sender);

        using var transport = new AzureServiceBusMessageTransport(client: client);
        var payload = Encoding.UTF8.GetBytes("payload");
        var metadata = new TransportMessageMetadata(
            MessageId: "MSG-1",
            MessageType: "OrderEvent",
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: "CORR-1",
            CausationId: "CAUSE-1",
            TraceParent: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            TenantId: "tenant-123",
            PartitionKey: "pk-abc",
            ContentType: "application/custom+json",
            SchemaVersion: 2,
            Headers: new Dictionary<string, string> { ["h1"] = "v1", ["h2"] = "v2" });

        var result = await transport.PublishRawAsync("my-topic", payload, metadata);

        result.IsSuccess.Should().BeTrue();
        await sender.Received(1).SendMessageAsync(
            Arg.Is<ServiceBusMessage>(msg =>
                msg.MessageId == "MSG-1" &&
                msg.Subject == "OrderEvent" &&
                msg.CorrelationId == "CORR-1" &&
                msg.ContentType == "application/custom+json" &&
                msg.PartitionKey == "pk-abc" &&
                (string)msg.ApplicationProperties["traceparent"] == "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01" &&
                (string)msg.ApplicationProperties["causation-id"] == "CAUSE-1" &&
                (string)msg.ApplicationProperties["tenant-id"] == "tenant-123" &&
                (string)msg.ApplicationProperties["schema-version"] == "2" &&
                (string)msg.ApplicationProperties["h1"] == "v1" &&
                (string)msg.ApplicationProperties["h2"] == "v2"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_SuccessWithNullAndEmptyProperties_UsesDefaults()
    {
        var client = Substitute.For<ServiceBusClient>();
        var sender = Substitute.For<ServiceBusSender>();
        client.CreateSender("my-topic").Returns(sender);

        using var transport = new AzureServiceBusMessageTransport(client: client);
        var payload = Encoding.UTF8.GetBytes("payload");
        var metadata = new TransportMessageMetadata(
            MessageId: "MSG-2",
            MessageType: "OrderEvent",
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: "CORR-2",
            CausationId: null,
            TraceParent: null,
            TenantId: string.Empty,
            PartitionKey: "   ",
            ContentType: null!,
            SchemaVersion: 1,
            Headers: null);

        var result = await transport.PublishRawAsync("my-topic", payload, metadata);

        result.IsSuccess.Should().BeTrue();
        await sender.Received(1).SendMessageAsync(
            Arg.Is<ServiceBusMessage>(msg =>
                msg.ContentType == "application/json" &&
                msg.PartitionKey == null &&
                !msg.ApplicationProperties.ContainsKey("traceparent") &&
                !msg.ApplicationProperties.ContainsKey("causation-id") &&
                !msg.ApplicationProperties.ContainsKey("tenant-id") &&
                !msg.ApplicationProperties.ContainsKey("schema-version") &&
                msg.ApplicationProperties.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_SameDestinationInvokedTwice_ReusesCachedSender()
    {
        var client = Substitute.For<ServiceBusClient>();
        var sender = Substitute.For<ServiceBusSender>();
        client.CreateSender("cached-topic").Returns(sender);

        using var transport = new AzureServiceBusMessageTransport(client: client);
        var metadata = TransportMessageMetadata.Create("test");

        await transport.PublishRawAsync("cached-topic", new byte[] { 1 }, metadata);
        await transport.PublishRawAsync("cached-topic", new byte[] { 2 }, metadata);

        client.Received(1).CreateSender("cached-topic");
        await sender.Received(2).SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_SenderThrowsException_LogsErrorAndReturnsFailure()
    {
        var client = Substitute.For<ServiceBusClient>();
        var sender = Substitute.For<ServiceBusSender>();
        var logger = Substitute.For<ILogger<AzureServiceBusMessageTransport>>();
        client.CreateSender("my-topic").Returns(sender);

        sender.SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ServiceBusException("Quota exceeded", ServiceBusFailureReason.QuotaExceeded));

        using var transport = new AzureServiceBusMessageTransport(client: client, logger: logger);

        var result = await transport.PublishRawAsync("my-topic", Encoding.UTF8.GetBytes("payload"), TransportMessageMetadata.Create("test"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AzureServiceBus.PublishFailed");
        result.Error.Description.Should().Contain("Quota exceeded");
        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Failed to publish message to Azure Service Bus destination")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task PublishRawAsync_WhenCancellationRequested_ThrowsOperationCanceledException()
    {
        var client = Substitute.For<ServiceBusClient>();
        var sender = Substitute.For<ServiceBusSender>();
        client.CreateSender("my-topic").Returns(sender);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        sender.SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        using var transport = new AzureServiceBusMessageTransport(client: client);

        Func<Task> act = async () => await transport.PublishRawAsync(
            "my-topic",
            Encoding.UTF8.GetBytes("payload"),
            TransportMessageMetadata.Create("test"),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region PublishBatchRawAsync

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PublishBatchRawAsync_InvalidDestination_ThrowsArgumentException(string? invalidDest)
    {
        var client = Substitute.For<ServiceBusClient>();
        using var transport = new AzureServiceBusMessageTransport(client: client);

        Func<Task> act = async () => await transport.PublishBatchRawAsync(
            invalidDest!,
            Array.Empty<(ReadOnlyMemory<byte>, TransportMessageMetadata)>());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PublishBatchRawAsync_NullBatch_ThrowsArgumentNullException()
    {
        var client = Substitute.For<ServiceBusClient>();
        using var transport = new AzureServiceBusMessageTransport(client: client);

        Func<Task> act = async () => await transport.PublishBatchRawAsync(
            "dest",
            null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishBatchRawAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        var client = Substitute.For<ServiceBusClient>();
        var transport = new AzureServiceBusMessageTransport(client: client);
        await transport.DisposeAsync();

        Func<Task> act = async () => await transport.PublishBatchRawAsync(
            "dest",
            new[] { (new ReadOnlyMemory<byte>(new byte[] { 1 }), TransportMessageMetadata.Create("test")) });

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task PublishBatchRawAsync_EmptyBatch_ReturnsSuccessImmediatelyWithoutSending()
    {
        var client = Substitute.For<ServiceBusClient>();
        var sender = Substitute.For<ServiceBusSender>();
        client.CreateSender("my-topic").Returns(sender);

        using var transport = new AzureServiceBusMessageTransport(client: client);

        var result = await transport.PublishBatchRawAsync(
            "my-topic",
            Array.Empty<(ReadOnlyMemory<byte>, TransportMessageMetadata)>());

        result.IsSuccess.Should().BeTrue();
        await sender.DidNotReceive().SendMessagesAsync(Arg.Any<IEnumerable<ServiceBusMessage>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishBatchRawAsync_ValidBatch_SendsAllMessages()
    {
        var client = Substitute.For<ServiceBusClient>();
        var sender = Substitute.For<ServiceBusSender>();
        client.CreateSender("my-topic").Returns(sender);

        using var transport = new AzureServiceBusMessageTransport(client: client);

        var batch = new List<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)>
        {
            (new byte[] { 1, 2 }, TransportMessageMetadata.Create("event.1")),
            (new byte[] { 3, 4 }, TransportMessageMetadata.Create("event.2"))
        };

        IEnumerable<ServiceBusMessage>? sentBatch = null;
        await sender.SendMessagesAsync(Arg.Do<IEnumerable<ServiceBusMessage>>(b => sentBatch = b), Arg.Any<CancellationToken>());

        var result = await transport.PublishBatchRawAsync("my-topic", batch);

        result.IsSuccess.Should().BeTrue();
        await sender.Received(1).SendMessagesAsync(Arg.Any<IEnumerable<ServiceBusMessage>>(), Arg.Any<CancellationToken>());
        sentBatch.Should().NotBeNull();
        var sentList = new List<ServiceBusMessage>(sentBatch!);
        sentList.Should().HaveCount(2);
        sentList[0].Subject.Should().Be("event.1");
        sentList[1].Subject.Should().Be("event.2");
    }

    [Fact]
    public async Task PublishBatchRawAsync_SenderThrowsException_LogsErrorAndReturnsFailure()
    {
        var client = Substitute.For<ServiceBusClient>();
        var sender = Substitute.For<ServiceBusSender>();
        var logger = Substitute.For<ILogger<AzureServiceBusMessageTransport>>();
        client.CreateSender("my-topic").Returns(sender);

        sender.SendMessagesAsync(Arg.Any<IEnumerable<ServiceBusMessage>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Batch rejected"));

        using var transport = new AzureServiceBusMessageTransport(client: client, logger: logger);

        var batch = new[] { (new ReadOnlyMemory<byte>(new byte[] { 1 }), TransportMessageMetadata.Create("test")) };
        var result = await transport.PublishBatchRawAsync("my-topic", batch);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AzureServiceBus.BatchPublishFailed");
        result.Error.Description.Should().Contain("Batch rejected");
        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Failed to publish batch of 1 messages to Azure Service Bus destination")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task PublishBatchRawAsync_WhenCancellationRequested_ThrowsOperationCanceledException()
    {
        var client = Substitute.For<ServiceBusClient>();
        var sender = Substitute.For<ServiceBusSender>();
        client.CreateSender("my-topic").Returns(sender);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        sender.SendMessagesAsync(Arg.Any<IEnumerable<ServiceBusMessage>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        using var transport = new AzureServiceBusMessageTransport(client: client);

        var batch = new[] { (new ReadOnlyMemory<byte>(new byte[] { 1 }), TransportMessageMetadata.Create("test")) };
        Func<Task> act = async () => await transport.PublishBatchRawAsync("my-topic", batch, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region DeferRawAsync

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DeferRawAsync_InvalidDestination_ThrowsArgumentException(string? invalidDest)
    {
        var client = Substitute.For<ServiceBusClient>();
        using var transport = new AzureServiceBusMessageTransport(client: client);

        Func<Task> act = async () => await transport.DeferRawAsync(
            invalidDest!,
            new byte[] { 1, 2 },
            TransportMessageMetadata.Create("test"),
            TimeSpan.FromSeconds(5));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DeferRawAsync_NullMetadata_ThrowsArgumentNullException()
    {
        var client = Substitute.For<ServiceBusClient>();
        using var transport = new AzureServiceBusMessageTransport(client: client);

        Func<Task> act = async () => await transport.DeferRawAsync(
            "dest",
            new byte[] { 1, 2 },
            null!,
            TimeSpan.FromSeconds(5));

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DeferRawAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        var client = Substitute.For<ServiceBusClient>();
        var transport = new AzureServiceBusMessageTransport(client: client);
        await transport.DisposeAsync();

        Func<Task> act = async () => await transport.DeferRawAsync(
            "orders.topic",
            new byte[] { 1, 2 },
            TransportMessageMetadata.Create("orders.created.v1"),
            TimeSpan.FromMinutes(1));

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task DeferRawAsync_ValidInput_SendsMessageWithScheduledEnqueueTime()
    {
        var client = Substitute.For<ServiceBusClient>();
        var sender = Substitute.For<ServiceBusSender>();
        client.CreateSender("orders.topic").Returns(sender);

        using var transport = new AzureServiceBusMessageTransport(client: client);
        var meta = TransportMessageMetadata.Create("orders.created.v1");
        var delay = TimeSpan.FromMinutes(5);

        ServiceBusMessage? sentMessage = null;
        await sender.SendMessageAsync(Arg.Do<ServiceBusMessage>(m => sentMessage = m), Arg.Any<CancellationToken>());

        var result = await transport.DeferRawAsync("orders.topic", new byte[] { 1, 2, 3 }, meta, delay);

        result.IsSuccess.Should().BeTrue();
        await sender.Received(1).SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>());
        sentMessage.Should().NotBeNull();
        sentMessage!.ScheduledEnqueueTime.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(4));
    }

    [Fact]
    public async Task DeferRawAsync_SenderThrows_LogsErrorAndReturnsFailure()
    {
        var client = Substitute.For<ServiceBusClient>();
        var sender = Substitute.For<ServiceBusSender>();
        var logger = Substitute.For<ILogger<AzureServiceBusMessageTransport>>();
        client.CreateSender("orders.topic").Returns(sender);
        sender.SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Service bus unavailable"));

        using var transport = new AzureServiceBusMessageTransport(client: client, logger: logger);
        var meta = TransportMessageMetadata.Create("orders.created.v1");

        var result = await transport.DeferRawAsync("orders.topic", new byte[] { 1, 2, 3 }, meta, TimeSpan.FromSeconds(30));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AzureServiceBus.DeferFailed");
        result.Error.Description.Should().Contain("Service bus unavailable");
        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Failed to defer message to Azure Service Bus destination")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task DeferRawAsync_WhenCancellationRequested_ThrowsOperationCanceledException()
    {
        var client = Substitute.For<ServiceBusClient>();
        var sender = Substitute.For<ServiceBusSender>();
        client.CreateSender("orders.topic").Returns(sender);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        sender.SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        using var transport = new AzureServiceBusMessageTransport(client: client);
        var meta = TransportMessageMetadata.Create("orders.created.v1");

        Func<Task> act = async () => await transport.DeferRawAsync("orders.topic", new byte[] { 1, 2 }, meta, TimeSpan.FromSeconds(10), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region SubscribeAsync & Message / Error Processing

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SubscribeAsync_InvalidDestination_ThrowsArgumentException(string? invalidDest)
    {
        var client = Substitute.For<ServiceBusClient>();
        using var transport = new AzureServiceBusMessageTransport(client: client);

        Func<Task> act = async () => await transport.SubscribeAsync(
            invalidDest!,
            (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            new TransportSubscriptionOptions());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SubscribeAsync_NullHandler_ThrowsArgumentNullException()
    {
        var client = Substitute.For<ServiceBusClient>();
        using var transport = new AzureServiceBusMessageTransport(client: client);

        Func<Task> act = async () => await transport.SubscribeAsync(
            "dest",
            null!,
            new TransportSubscriptionOptions());

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SubscribeAsync_NullOptions_ThrowsArgumentNullException()
    {
        var client = Substitute.For<ServiceBusClient>();
        using var transport = new AzureServiceBusMessageTransport(client: client);

        Func<Task> act = async () => await transport.SubscribeAsync(
            "dest",
            (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SubscribeAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        var client = Substitute.For<ServiceBusClient>();
        var transport = new AzureServiceBusMessageTransport(client: client);
        await transport.DisposeAsync();

        Func<Task> act = async () => await transport.SubscribeAsync(
            "dest",
            (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            new TransportSubscriptionOptions());

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task SubscribeAsync_WithOptions_ConfiguresProcessorOptionsWithClampingAndDefaults()
    {
        var client = Substitute.For<ServiceBusClient>();
        var processor = Substitute.For<ServiceBusProcessor>();
        ServiceBusProcessorOptions? capturedOptions = null;

        client.CreateProcessor("clamped-queue", Arg.Do<ServiceBusProcessorOptions>(opt => capturedOptions = opt))
            .Returns(processor);

        using var transport = new AzureServiceBusMessageTransport(client: client);

        // Options with negative values should be clamped: MaxConcurrency <= 0 -> 1, PrefetchCount < 0 -> 0
        var subResult = await transport.SubscribeAsync(
            "clamped-queue",
            (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            new TransportSubscriptionOptions { MaxConcurrency = 0, PrefetchCount = -5 });

        subResult.IsSuccess.Should().BeTrue();
        capturedOptions.Should().NotBeNull();
        capturedOptions!.MaxConcurrentCalls.Should().Be(1);
        capturedOptions.PrefetchCount.Should().Be(0);
        capturedOptions.AutoCompleteMessages.Should().BeFalse();
    }

    [Fact]
    public async Task SubscribeAsync_ProcessorThrowsOnStart_LogsAndReturnsFailure()
    {
        var client = Substitute.For<ServiceBusClient>();
        var processor = Substitute.For<ServiceBusProcessor>();
        var logger = Substitute.For<ILogger<AzureServiceBusMessageTransport>>();

        client.CreateProcessor("bad-queue", Arg.Any<ServiceBusProcessorOptions>()).Returns(processor);
        processor.StartProcessingAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Connection failed"));

        using var transport = new AzureServiceBusMessageTransport(client: client, logger: logger);

        var result = await transport.SubscribeAsync(
            "bad-queue",
            (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            new TransportSubscriptionOptions());

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AzureServiceBus.SubscribeFailed");
        result.Error.Description.Should().Contain("Connection failed");
        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Failed to subscribe to Azure Service Bus destination")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task SubscribeAsync_WhenCancellationRequested_ThrowsOperationCanceledException()
    {
        var client = Substitute.For<ServiceBusClient>();
        var processor = Substitute.For<ServiceBusProcessor>();
        client.CreateProcessor("bad-queue", Arg.Any<ServiceBusProcessorOptions>()).Returns(processor);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        processor.StartProcessingAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        using var transport = new AzureServiceBusMessageTransport(client: client);

        Func<Task> act = async () => await transport.SubscribeAsync(
            "bad-queue",
            (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            new TransportSubscriptionOptions(),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SubscribeAsync_ProcessMessageAsync_WhenAck_CompletesMessage()
    {
        var client = Substitute.For<ServiceBusClient>();
        var processor = Substitute.For<ServiceBusProcessor>();
        client.CreateProcessor("event-queue", Arg.Any<ServiceBusProcessorOptions>()).Returns(processor);

        using var transport = new AzureServiceBusMessageTransport(client: client);

        await transport.SubscribeAsync(
            "event-queue",
            (body, meta, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            new TransportSubscriptionOptions());

        var processMessageField = typeof(ServiceBusProcessor).GetField("_processMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy)!;
        var processMessageHandler = (Func<ProcessMessageEventArgs, Task>)processMessageField.GetValue(processor)!;

        var receivedMsg = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData(Encoding.UTF8.GetBytes("payload")),
            messageId: "MSG-1");

        var receiver = Substitute.For<ServiceBusReceiver>();
        var processArgs = AzureServiceBusTestFactory.CreateProcessMessageEventArgs(receivedMsg, receiver, CancellationToken.None);

        await processMessageHandler(processArgs);

        await receiver.Received(1).CompleteMessageAsync(receivedMsg, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ProcessMessageAsync_WhenNackRequeue_AbandonsMessage()
    {
        var client = Substitute.For<ServiceBusClient>();
        var processor = Substitute.For<ServiceBusProcessor>();
        client.CreateProcessor("event-queue", Arg.Any<ServiceBusProcessorOptions>()).Returns(processor);

        using var transport = new AzureServiceBusMessageTransport(client: client);

        await transport.SubscribeAsync(
            "event-queue",
            (body, meta, ct) => ValueTask.FromResult(TransportAckResult.NackRequeue),
            new TransportSubscriptionOptions());

        var processMessageField = typeof(ServiceBusProcessor).GetField("_processMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy)!;
        var processMessageHandler = (Func<ProcessMessageEventArgs, Task>)processMessageField.GetValue(processor)!;

        var receivedMsg = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData(Encoding.UTF8.GetBytes("payload")),
            messageId: "MSG-1");

        var receiver = Substitute.For<ServiceBusReceiver>();
        var processArgs = AzureServiceBusTestFactory.CreateProcessMessageEventArgs(receivedMsg, receiver, CancellationToken.None);

        await processMessageHandler(processArgs);

        await receiver.Received(1).AbandonMessageAsync(receivedMsg, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ProcessMessageAsync_WhenDeadLetter_DeadLettersMessage()
    {
        var client = Substitute.For<ServiceBusClient>();
        var processor = Substitute.For<ServiceBusProcessor>();
        client.CreateProcessor("event-queue", Arg.Any<ServiceBusProcessorOptions>()).Returns(processor);

        using var transport = new AzureServiceBusMessageTransport(client: client);

        await transport.SubscribeAsync(
            "event-queue",
            (body, meta, ct) => ValueTask.FromResult(TransportAckResult.DeadLetter),
            new TransportSubscriptionOptions());

        var processMessageField = typeof(ServiceBusProcessor).GetField("_processMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy)!;
        var processMessageHandler = (Func<ProcessMessageEventArgs, Task>)processMessageField.GetValue(processor)!;

        var receivedMsg = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData(Encoding.UTF8.GetBytes("payload")),
            messageId: "MSG-1");

        var receiver = Substitute.For<ServiceBusReceiver>();
        var processArgs = AzureServiceBusTestFactory.CreateProcessMessageEventArgs(receivedMsg, receiver, CancellationToken.None);

        await processMessageHandler(processArgs);

        await receiver.Received(1).DeadLetterMessageAsync(receivedMsg, "ProcessingError", "Message processing rejected by handler.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ProcessMessageAsync_WhenUnrecognizedAckResult_DeadLettersMessage()
    {
        var client = Substitute.For<ServiceBusClient>();
        var processor = Substitute.For<ServiceBusProcessor>();
        client.CreateProcessor("event-queue", Arg.Any<ServiceBusProcessorOptions>()).Returns(processor);

        using var transport = new AzureServiceBusMessageTransport(client: client);

        await transport.SubscribeAsync(
            "event-queue",
            (body, meta, ct) => ValueTask.FromResult((TransportAckResult)999),
            new TransportSubscriptionOptions());

        var processMessageField = typeof(ServiceBusProcessor).GetField("_processMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy)!;
        var processMessageHandler = (Func<ProcessMessageEventArgs, Task>)processMessageField.GetValue(processor)!;

        var receivedMsg = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData(Encoding.UTF8.GetBytes("payload")),
            messageId: "MSG-1");

        var receiver = Substitute.For<ServiceBusReceiver>();
        var processArgs = AzureServiceBusTestFactory.CreateProcessMessageEventArgs(receivedMsg, receiver, CancellationToken.None);

        await processMessageHandler(processArgs);

        await receiver.Received(1).DeadLetterMessageAsync(receivedMsg, "ProcessingError", "Message processing rejected by handler.", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ProcessMessageAsync_ExtractsAllHeadersAndMetadata()
    {
        var client = Substitute.For<ServiceBusClient>();
        var processor = Substitute.For<ServiceBusProcessor>();
        client.CreateProcessor("event-queue", Arg.Any<ServiceBusProcessorOptions>()).Returns(processor);

        TransportMessageMetadata? capturedMetadata = null;

        using var transport = new AzureServiceBusMessageTransport(client: client);

        await transport.SubscribeAsync(
            "event-queue",
            (body, meta, ct) =>
            {
                capturedMetadata = meta;
                return ValueTask.FromResult(TransportAckResult.Ack);
            },
            new TransportSubscriptionOptions());

        var processMessageField = typeof(ServiceBusProcessor).GetField("_processMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy)!;
        var processMessageHandler = (Func<ProcessMessageEventArgs, Task>)processMessageField.GetValue(processor)!;

        var properties = new Dictionary<string, object?>
        {
            ["traceparent"] = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            ["causation-id"] = "CAUSE-1",
            ["tenant-id"] = "TENANT-1",
            ["partition-key"] = "pk-from-header",
            ["schema-version"] = "3",
            ["null-prop"] = null,
            ["custom-header"] = "custom-val"
        };

        var enqueuedTime = DateTimeOffset.UtcNow;
        var receivedMsg = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData(Encoding.UTF8.GetBytes("payload")),
            messageId: "MSG-AZ-1",
            partitionKey: null,
            correlationId: "CORR-AZ-1",
            subject: "OrderEvent",
            contentType: "application/custom-json",
            enqueuedTime: enqueuedTime,
            properties: properties!);

        var receiver = Substitute.For<ServiceBusReceiver>();
        var processArgs = AzureServiceBusTestFactory.CreateProcessMessageEventArgs(receivedMsg, receiver, CancellationToken.None);

        await processMessageHandler(processArgs);

        capturedMetadata.Should().NotBeNull();
        capturedMetadata!.MessageId.Should().Be("MSG-AZ-1");
        capturedMetadata.MessageType.Should().Be("OrderEvent");
        capturedMetadata.Timestamp.Should().Be(enqueuedTime);
        capturedMetadata.CorrelationId.Should().Be("CORR-AZ-1");
        capturedMetadata.CausationId.Should().Be("CAUSE-1");
        capturedMetadata.TraceParent.Should().Be("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
        capturedMetadata.TenantId.Should().Be("TENANT-1");
        capturedMetadata.PartitionKey.Should().Be("pk-from-header");
        capturedMetadata.ContentType.Should().Be("application/custom-json");
        capturedMetadata.SchemaVersion.Should().Be(3);
        capturedMetadata.Headers.Should().ContainKey("custom-header");
        capturedMetadata.Headers!["null-prop"].Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeAsync_ProcessMessageAsync_WhenSchemaVersionIsInvalidString_FallsBackToVersionOne()
    {
        var client = Substitute.For<ServiceBusClient>();
        var processor = Substitute.For<ServiceBusProcessor>();
        client.CreateProcessor("event-queue", Arg.Any<ServiceBusProcessorOptions>()).Returns(processor);

        TransportMessageMetadata? capturedMetadata = null;

        using var transport = new AzureServiceBusMessageTransport(client: client);

        await transport.SubscribeAsync(
            "event-queue",
            (body, meta, ct) =>
            {
                capturedMetadata = meta;
                return ValueTask.FromResult(TransportAckResult.Ack);
            },
            new TransportSubscriptionOptions());

        var processMessageField = typeof(ServiceBusProcessor).GetField("_processMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy)!;
        var processMessageHandler = (Func<ProcessMessageEventArgs, Task>)processMessageField.GetValue(processor)!;

        var properties = new Dictionary<string, object?>
        {
            ["schema-version"] = "invalid_version_string"
        };

        var receivedMsg = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData(Encoding.UTF8.GetBytes("payload")),
            properties: properties!);

        var receiver = Substitute.For<ServiceBusReceiver>();
        var processArgs = AzureServiceBusTestFactory.CreateProcessMessageEventArgs(receivedMsg, receiver, CancellationToken.None);

        await processMessageHandler(processArgs);

        capturedMetadata.Should().NotBeNull();
        capturedMetadata!.SchemaVersion.Should().Be(1);
    }

    [Fact]
    public async Task SubscribeAsync_ProcessMessageAsync_PartitionKeyPrecedence_PrefersMessagePartitionKeyOverHeader()
    {
        var client = Substitute.For<ServiceBusClient>();
        var processor = Substitute.For<ServiceBusProcessor>();
        client.CreateProcessor("event-queue", Arg.Any<ServiceBusProcessorOptions>()).Returns(processor);

        TransportMessageMetadata? capturedMetadata = null;

        using var transport = new AzureServiceBusMessageTransport(client: client);

        await transport.SubscribeAsync(
            "event-queue",
            (body, meta, ct) =>
            {
                capturedMetadata = meta;
                return ValueTask.FromResult(TransportAckResult.Ack);
            },
            new TransportSubscriptionOptions());

        var processMessageField = typeof(ServiceBusProcessor).GetField("_processMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy)!;
        var processMessageHandler = (Func<ProcessMessageEventArgs, Task>)processMessageField.GetValue(processor)!;

        var properties = new Dictionary<string, object?>
        {
            ["partition-key"] = "pk-header"
        };

        var receivedMsg = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData(Encoding.UTF8.GetBytes("payload")),
            partitionKey: "pk-msg",
            properties: properties!);

        var receiver = Substitute.For<ServiceBusReceiver>();
        var processArgs = AzureServiceBusTestFactory.CreateProcessMessageEventArgs(receivedMsg, receiver, CancellationToken.None);

        await processMessageHandler(processArgs);

        capturedMetadata.Should().NotBeNull();
        capturedMetadata!.PartitionKey.Should().Be("pk-msg");
    }

    [Fact]
    public async Task SubscribeAsync_ProcessMessageAsync_MinimalMessage_AppliesDefaults()
    {
        var client = Substitute.For<ServiceBusClient>();
        var processor = Substitute.For<ServiceBusProcessor>();
        client.CreateProcessor("event-queue", Arg.Any<ServiceBusProcessorOptions>()).Returns(processor);

        TransportMessageMetadata? capturedMetadata = null;

        using var transport = new AzureServiceBusMessageTransport(client: client);

        await transport.SubscribeAsync(
            "event-queue",
            (body, meta, ct) =>
            {
                capturedMetadata = meta;
                return ValueTask.FromResult(TransportAckResult.Ack);
            },
            new TransportSubscriptionOptions());

        var processMessageField = typeof(ServiceBusProcessor).GetField("_processMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy)!;
        var processMessageHandler = (Func<ProcessMessageEventArgs, Task>)processMessageField.GetValue(processor)!;

        var minimalMsg = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData(Array.Empty<byte>()),
            messageId: null,
            partitionKey: null,
            correlationId: null,
            subject: null,
            contentType: null,
            properties: null);

        var receiver = Substitute.For<ServiceBusReceiver>();
        var processArgs = AzureServiceBusTestFactory.CreateProcessMessageEventArgs(minimalMsg, receiver, CancellationToken.None);

        await processMessageHandler(processArgs);

        capturedMetadata.Should().NotBeNull();
        capturedMetadata!.MessageId.Should().NotBeNullOrWhiteSpace();
        capturedMetadata.MessageId.Length.Should().Be(32);
        Guid.TryParseExact(capturedMetadata.MessageId, "N", out _).Should().BeTrue();
        capturedMetadata.MessageType.Should().Be("application/octet-stream");
        capturedMetadata.CorrelationId.Should().NotBeNullOrWhiteSpace();
        capturedMetadata.CorrelationId.Length.Should().Be(32);
        Guid.TryParseExact(capturedMetadata.CorrelationId, "N", out _).Should().BeTrue();
        capturedMetadata.ContentType.Should().Be("application/json");
        capturedMetadata.Headers.Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeAsync_ProcessErrorAsync_LogsError()
    {
        var client = Substitute.For<ServiceBusClient>();
        var processor = Substitute.For<ServiceBusProcessor>();
        var logger = Substitute.For<ILogger<AzureServiceBusMessageTransport>>();
        client.CreateProcessor("error-queue", Arg.Any<ServiceBusProcessorOptions>()).Returns(processor);

        using var transport = new AzureServiceBusMessageTransport(client: client, logger: logger);

        await transport.SubscribeAsync(
            "error-queue",
            (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            new TransportSubscriptionOptions());

        var processErrorField = typeof(ServiceBusProcessor).GetField("_processErrorAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy)!;
        var processErrorHandler = (Func<ProcessErrorEventArgs, Task>)processErrorField.GetValue(processor)!;

        var exception = new InvalidOperationException("Processor failure");
        var errorArgs = new ProcessErrorEventArgs(
            exception,
            ServiceBusErrorSource.Receive,
            "test.servicebus.windows.net",
            "error-queue",
            CancellationToken.None);

        await processErrorHandler(errorArgs);

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Azure Service Bus processor error")),
            exception,
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region Dispose & DisposeAsync

    [Fact]
    public async Task DisposeAsync_WithActiveProcessorsAndSenders_StopsProcessorsAndDisposesSendersAndClient()
    {
        var client = Substitute.For<ServiceBusClient>();
        var sender1 = Substitute.For<ServiceBusSender>();
        var sender2 = Substitute.For<ServiceBusSender>();
        var processor1 = Substitute.For<ServiceBusProcessor>();
        var processor2 = Substitute.For<ServiceBusProcessor>();

        client.CreateSender("topic1").Returns(sender1);
        client.CreateSender("topic2").Returns(sender2);
        client.CreateProcessor("topic1", Arg.Any<ServiceBusProcessorOptions>()).Returns(processor1);
        client.CreateProcessor("topic2", Arg.Any<ServiceBusProcessorOptions>()).Returns(processor2);

        var transport = new AzureServiceBusMessageTransport(client: client);
        await transport.PublishRawAsync("topic1", new byte[] { 1 }, TransportMessageMetadata.Create("t1"));
        await transport.PublishRawAsync("topic2", new byte[] { 2 }, TransportMessageMetadata.Create("t2"));
        await transport.SubscribeAsync("topic1", (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());
        await transport.SubscribeAsync("topic2", (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());

        await transport.DisposeAsync();
        await transport.DisposeAsync(); // Idempotent second call

        await processor1.Received(1).StopProcessingAsync();
        await processor1.Received(1).DisposeAsync();
        await processor2.Received(1).StopProcessingAsync();
        await processor2.Received(1).DisposeAsync();

        await sender1.Received(1).DisposeAsync();
        await sender2.Received(1).DisposeAsync();

        await client.Received(1).DisposeAsync();

        Func<Task> act = async () => await transport.PublishRawAsync("topic1", new byte[] { 1 }, TransportMessageMetadata.Create("t1"));
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Dispose_Synchronous_CleansUpMultipleProcessorsAndSendersAndClient()
    {
        var client = Substitute.For<ServiceBusClient>();
        var sender1 = Substitute.For<ServiceBusSender>();
        var sender2 = Substitute.For<ServiceBusSender>();
        var processor1 = Substitute.For<ServiceBusProcessor>();
        var processor2 = Substitute.For<ServiceBusProcessor>();

        client.CreateSender("topic1").Returns(sender1);
        client.CreateSender("topic2").Returns(sender2);
        client.CreateProcessor("topic1", Arg.Any<ServiceBusProcessorOptions>()).Returns(processor1);
        client.CreateProcessor("topic2", Arg.Any<ServiceBusProcessorOptions>()).Returns(processor2);

        var transport = new AzureServiceBusMessageTransport(client: client);
        await transport.PublishRawAsync("topic1", new byte[] { 1 }, TransportMessageMetadata.Create("t1"));
        await transport.PublishRawAsync("topic2", new byte[] { 2 }, TransportMessageMetadata.Create("t2"));
        await transport.SubscribeAsync("topic1", (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());
        await transport.SubscribeAsync("topic2", (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());

        transport.Dispose();
        transport.Dispose(); // Idempotent second call

        await processor1.Received(1).DisposeAsync();
        await processor2.Received(1).DisposeAsync();
        await sender1.Received(1).DisposeAsync();
        await sender2.Received(1).DisposeAsync();
        await client.Received(1).DisposeAsync();

        Func<Task> act = async () => await transport.PublishRawAsync("topic1", new byte[] { 1 }, TransportMessageMetadata.Create("t1"));
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Dispose_ThenDisposeAsync_IsIdempotent()
    {
        var client = Substitute.For<ServiceBusClient>();
        var transport = new AzureServiceBusMessageTransport(client: client);

        transport.Dispose();
        await transport.DisposeAsync();

        await client.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_ThenDispose_IsIdempotent()
    {
        var client = Substitute.For<ServiceBusClient>();
        var transport = new AzureServiceBusMessageTransport(client: client);

        await transport.DisposeAsync();
        transport.Dispose();

        await client.Received(1).DisposeAsync();
    }

    [Fact]
    public void Constructor_WithFullyQualifiedNamespaceAndCustomCredential_InitializesSuccessfully()
    {
        var credential = Substitute.For<global::Azure.Core.TokenCredential>();
        var options = Options.Create(new AzureServiceBusTransportOptions
        {
            FullyQualifiedNamespace = "test.servicebus.windows.net",
            Credential = credential
        });

        using var transport = new AzureServiceBusMessageTransport(options: options);
        transport.Should().NotBeNull();
        var clientField = typeof(AzureServiceBusMessageTransport).GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance);
        var client = clientField?.GetValue(transport) as ServiceBusClient;
        client.Should().NotBeNull();
        client!.FullyQualifiedNamespace.Should().Be("test.servicebus.windows.net");
        var connection = typeof(ServiceBusClient).GetProperty("Connection", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(client);
        var cred = connection?.GetType().GetProperty("Credential", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(connection)
            ?? connection?.GetType().GetField("_credential", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(connection);
        if (cred is not null)
        {
            cred.Should().BeSameAs(credential);
        }
        options.Value.Credential.Should().BeSameAs(credential);
    }

    [Fact]
    public async Task DisposeAsync_WithSendersAndProcessors_DisposesAndClearsCollections()
    {
        var client = Substitute.For<ServiceBusClient>();
        var sender = Substitute.For<ServiceBusSender>();
        var processor = Substitute.For<ServiceBusProcessor>();
        client.CreateSender("queue").Returns(sender);
        client.CreateProcessor("queue", Arg.Any<ServiceBusProcessorOptions>()).Returns(processor);

        var transport = new AzureServiceBusMessageTransport(client: client);
        await transport.PublishRawAsync("queue", new byte[] { 1 }, TransportMessageMetadata.Create("msg"));
        await transport.SubscribeAsync("queue", (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());

        var sendersField = typeof(AzureServiceBusMessageTransport).GetField("_senders", BindingFlags.NonPublic | BindingFlags.Instance);
        var procField = typeof(AzureServiceBusMessageTransport).GetField("_processors", BindingFlags.NonPublic | BindingFlags.Instance);
        var senders = (System.Collections.IDictionary)sendersField!.GetValue(transport)!;
        var processors = (System.Collections.IDictionary)procField!.GetValue(transport)!;

        senders.Count.Should().Be(1);
        processors.Count.Should().Be(1);

        await transport.DisposeAsync();

        senders.Count.Should().Be(0);
        processors.Count.Should().Be(0);
        await sender.Received(1).DisposeAsync();
        await processor.Received(1).DisposeAsync();
        await client.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Dispose_WithSendersAndProcessors_DisposesAndClearsCollections()
    {
        var client = Substitute.For<ServiceBusClient>();
        var sender = Substitute.For<ServiceBusSender>();
        var processor = Substitute.For<ServiceBusProcessor>();
        client.CreateSender("queue").Returns(sender);
        client.CreateProcessor("queue", Arg.Any<ServiceBusProcessorOptions>()).Returns(processor);

        var transport = new AzureServiceBusMessageTransport(client: client);
        await transport.PublishRawAsync("queue", new byte[] { 1 }, TransportMessageMetadata.Create("msg"));
        await transport.SubscribeAsync("queue", (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());

        var sendersField = typeof(AzureServiceBusMessageTransport).GetField("_senders", BindingFlags.NonPublic | BindingFlags.Instance);
        var procField = typeof(AzureServiceBusMessageTransport).GetField("_processors", BindingFlags.NonPublic | BindingFlags.Instance);
        var senders = (System.Collections.IDictionary)sendersField!.GetValue(transport)!;
        var processors = (System.Collections.IDictionary)procField!.GetValue(transport)!;

        senders.Count.Should().Be(1);
        processors.Count.Should().Be(1);

        transport.Dispose();

        senders.Count.Should().Be(0);
        processors.Count.Should().Be(0);
        _ = sender.Received(1).DisposeAsync();
        _ = processor.Received(1).DisposeAsync();
        _ = client.Received(1).DisposeAsync();
    }

    #endregion
}

