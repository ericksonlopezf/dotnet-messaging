// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.SQS;
using Amazon.SQS.Model;
using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Messaging.Transport.AwsSqs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace EricksonLopez.Messaging.AwsSqs.Tests;

[Trait("Category", "Unit")]
public class AwsSqsMessageTransportTests
{
    #region Constructor

    [Fact]
    public void Constructor_Default_InitializesDefaults()
    {
        using var transport = new AwsSqsMessageTransport();
        transport.Should().NotBeNull();
        transport.Should().BeAssignableTo<IAsyncDisposable>();
    }

    [Fact]
    public void Constructor_WithOptions_CustomServiceUrl_InitializesSuccessfully()
    {
        var options = Options.Create(new AwsSqsTransportOptions
        {
            ServiceUrl = "http://localhost:4566"
        });

        using var transport = new AwsSqsMessageTransport(options: options);
        transport.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithOptions_CustomRegion_InitializesSuccessfully()
    {
        var options = Options.Create(new AwsSqsTransportOptions
        {
            Region = "eu-central-1"
        });

        using var transport = new AwsSqsMessageTransport(options: options);
        transport.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithOptions_WhitespaceServiceUrl_FallsBackToRegion()
    {
        var options = Options.Create(new AwsSqsTransportOptions
        {
            ServiceUrl = "   ",
            Region = "eu-west-1"
        });

        using var transport = new AwsSqsMessageTransport(options: options);
        var field = typeof(AwsSqsMessageTransport).GetField("_sqsClient", BindingFlags.NonPublic | BindingFlags.Instance);
        var client = field?.GetValue(transport) as AmazonSQSClient;
        client.Should().NotBeNull();
        client!.Config.RegionEndpoint.SystemName.Should().Be("eu-west-1");
    }

    [Fact]
    public async Task Constructor_WithExplicitClientAndLogger_UsesProvidedInstances()
    {
        var client = Substitute.For<IAmazonSQS>();
        client.SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SendMessageResponse { HttpStatusCode = System.Net.HttpStatusCode.OK }));
        var logger = Substitute.For<ILogger<AwsSqsMessageTransport>>();

        using var transport = new AwsSqsMessageTransport(sqsClient: client, logger: logger);
        var result = await transport.PublishRawAsync("https://sqs.us-east-1.amazonaws.com/123/queue", new byte[] { 1 }, TransportMessageMetadata.Create("test"));

        result.IsSuccess.Should().BeTrue();
        await client.Received(1).SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region PublishRawAsync

    [Fact]
    public async Task PublishRawAsync_Disposed_ThrowsObjectDisposedException()
    {
        var client = Substitute.For<IAmazonSQS>();
        var transport = new AwsSqsMessageTransport(sqsClient: client);
        transport.Dispose();

        Func<Task> act = async () => await transport.PublishRawAsync("queue-url", new byte[] { 1 }, TransportMessageMetadata.Create("test"));
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PublishRawAsync_InvalidDestination_ThrowsArgumentException(string? destination)
    {
        var client = Substitute.For<IAmazonSQS>();
        using var transport = new AwsSqsMessageTransport(sqsClient: client);

        Func<Task> act = async () => await transport.PublishRawAsync(destination!, new byte[] { 1 }, TransportMessageMetadata.Create("test"));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PublishRawAsync_NullMetadata_ThrowsArgumentNullException()
    {
        var client = Substitute.For<IAmazonSQS>();
        using var transport = new AwsSqsMessageTransport(sqsClient: client);

        Func<Task> act = async () => await transport.PublishRawAsync("queue-url", new byte[] { 1 }, null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("metadata");
    }

    [Fact]
    public async Task PublishRawAsync_ValidMessageWithRichMetadata_SendsBase64BodyAndAllAttributes()
    {
        var client = Substitute.For<IAmazonSQS>();
        SendMessageRequest? capturedRequest = null;
        client.SendMessageAsync(Arg.Do<SendMessageRequest>(r => capturedRequest = r), Arg.Any<CancellationToken>())
              .Returns(new SendMessageResponse());

        using var transport = new AwsSqsMessageTransport(sqsClient: client);
        var rawBytes = Encoding.UTF8.GetBytes("hello aws");
        var metadata = new TransportMessageMetadata(
            MessageId: "msg-123",
            MessageType: "OrderCreated",
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: "corr-456",
            CausationId: "cause-789",
            TraceParent: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
            TenantId: "tenant-789",
            PartitionKey: "pk-1",
            ContentType: "application/custom+json",
            SchemaVersion: 3,
            Headers: new Dictionary<string, string> { ["x-custom"] = "custom-val" });

        var result = await transport.PublishRawAsync("https://sqs.us-east-1.amazonaws.com/123/my-queue", rawBytes, metadata);

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.QueueUrl.Should().Be("https://sqs.us-east-1.amazonaws.com/123/my-queue");
        capturedRequest.MessageBody.Should().Be(Convert.ToBase64String(rawBytes));
        capturedRequest.MessageAttributes["message-id"].DataType.Should().Be("String");
        capturedRequest.MessageAttributes["message-id"].StringValue.Should().Be("msg-123");
        capturedRequest.MessageAttributes["message-type"].DataType.Should().Be("String");
        capturedRequest.MessageAttributes["message-type"].StringValue.Should().Be("OrderCreated");
        capturedRequest.MessageAttributes["correlation-id"].DataType.Should().Be("String");
        capturedRequest.MessageAttributes["correlation-id"].StringValue.Should().Be("corr-456");
        capturedRequest.MessageAttributes["tenant-id"].DataType.Should().Be("String");
        capturedRequest.MessageAttributes["tenant-id"].StringValue.Should().Be("tenant-789");
        capturedRequest.MessageAttributes["traceparent"].DataType.Should().Be("String");
        capturedRequest.MessageAttributes["traceparent"].StringValue.Should().Be("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
        capturedRequest.MessageAttributes["causation-id"].DataType.Should().Be("String");
        capturedRequest.MessageAttributes["causation-id"].StringValue.Should().Be("cause-789");
        capturedRequest.MessageAttributes["content-type"].DataType.Should().Be("String");
        capturedRequest.MessageAttributes["content-type"].StringValue.Should().Be("application/custom+json");
        capturedRequest.MessageAttributes["schema-version"].DataType.Should().Be("String");
        capturedRequest.MessageAttributes["schema-version"].StringValue.Should().Be("3");
        capturedRequest.MessageAttributes["x-custom"].DataType.Should().Be("String");
        capturedRequest.MessageAttributes["x-custom"].StringValue.Should().Be("custom-val");
        capturedRequest.MessageGroupId.Should().Be("pk-1");
        capturedRequest.MessageDeduplicationId.Should().Be("msg-123");
    }

    [Fact]
    public async Task PublishRawAsync_PartitionKeyWithNullMessageId_GeneratesGuidDeduplicationId()
    {
        var client = Substitute.For<IAmazonSQS>();
        SendMessageRequest? capturedRequest = null;
        client.SendMessageAsync(Arg.Do<SendMessageRequest>(r => capturedRequest = r), Arg.Any<CancellationToken>())
              .Returns(new SendMessageResponse());

        using var transport = new AwsSqsMessageTransport(sqsClient: client);
        var rawBytes = new byte[] { 1 };
        var metadata = new TransportMessageMetadata(
            MessageId: string.Empty,
            MessageType: "OrderCreated",
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: string.Empty,
            CausationId: null,
            TraceParent: null,
            TenantId: null,
            PartitionKey: "group-1",
            ContentType: "application/json",
            SchemaVersion: 1,
            Headers: null);

        var result = await transport.PublishRawAsync("https://sqs.my-queue", rawBytes, metadata);

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.MessageGroupId.Should().Be("group-1");
        capturedRequest.MessageDeduplicationId.Should().NotBeNullOrWhiteSpace();
        capturedRequest.MessageDeduplicationId.Length.Should().Be(32);
        Guid.TryParseExact(capturedRequest.MessageDeduplicationId, "N", out _).Should().BeTrue();
    }

    [Fact]
    public async Task PublishRawAsync_MinimalMetadata_OmitsOptionalAttributesAndGroupId()
    {
        var client = Substitute.For<IAmazonSQS>();
        SendMessageRequest? capturedRequest = null;
        client.SendMessageAsync(Arg.Do<SendMessageRequest>(r => capturedRequest = r), Arg.Any<CancellationToken>())
              .Returns(new SendMessageResponse());

        using var transport = new AwsSqsMessageTransport(sqsClient: client);
        var rawBytes = new byte[] { 1 };
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

        var result = await transport.PublishRawAsync("https://sqs.my-queue", rawBytes, metadata);

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.MessageAttributes.Should().BeEmpty();
        capturedRequest.MessageGroupId.Should().BeNull();
        capturedRequest.MessageDeduplicationId.Should().BeNull();
    }

    [Fact]
    public async Task PublishRawAsync_WhenSqsThrows_LogsErrorAndReturnsFailureResult()
    {
        var client = Substitute.For<IAmazonSQS>();
        var logger = Substitute.For<ILogger<AwsSqsMessageTransport>>();
        client.SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>())
              .ThrowsAsync(new AmazonSQSException("Network timeout"));

        using var transport = new AwsSqsMessageTransport(sqsClient: client, logger: logger);

        var result = await transport.PublishRawAsync("https://sqs.us-east-1.amazonaws.com/123/my-queue", new byte[] { 1 }, TransportMessageMetadata.Create("test"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AwsSqs.PublishFailed");
        result.Error.Description.Should().Contain("Network timeout");

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Failed to publish message to AWS SQS queue")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task PublishRawAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var client = Substitute.For<IAmazonSQS>();
        var logger = Substitute.For<ILogger<AwsSqsMessageTransport>>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        client.SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>())
              .ThrowsAsync(new OperationCanceledException(cts.Token));

        using var transport = new AwsSqsMessageTransport(sqsClient: client, logger: logger);

        Func<Task> act = async () => await transport.PublishRawAsync("queue-url", new byte[] { 1 }, TransportMessageMetadata.Create("test"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        logger.DidNotReceive().Log(LogLevel.Error, Arg.Any<EventId>(), Arg.Any<object>(), Arg.Any<Exception>(), Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region SubscribeAsync

    [Fact]
    public async Task SubscribeAsync_Disposed_ThrowsObjectDisposedException()
    {
        var client = Substitute.For<IAmazonSQS>();
        var transport = new AwsSqsMessageTransport(sqsClient: client);
        transport.Dispose();

        Func<Task> act = async () => await transport.SubscribeAsync("queue-url", (_, _, _) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SubscribeAsync_InvalidDestination_ThrowsArgumentException(string? destination)
    {
        var client = Substitute.For<IAmazonSQS>();
        using var transport = new AwsSqsMessageTransport(sqsClient: client);

        Func<Task> act = async () => await transport.SubscribeAsync(destination!, (_, _, _) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SubscribeAsync_NullHandler_ThrowsArgumentNullException()
    {
        var client = Substitute.For<IAmazonSQS>();
        using var transport = new AwsSqsMessageTransport(sqsClient: client);

        Func<Task> act = async () => await transport.SubscribeAsync("queue-url", null!, new TransportSubscriptionOptions());
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("messageHandler");
    }

    [Fact]
    public async Task SubscribeAsync_NullOptions_ThrowsArgumentNullException()
    {
        var client = Substitute.For<IAmazonSQS>();
        using var transport = new AwsSqsMessageTransport(sqsClient: client);

        Func<Task> act = async () => await transport.SubscribeAsync("queue-url", (_, _, _) => ValueTask.FromResult(TransportAckResult.Ack), null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task SubscribeAsync_ReceiveMessageRequest_SetsAllConfiguredOptions()
    {
        var client = Substitute.For<IAmazonSQS>();
        using var cts = new CancellationTokenSource();

        var options = Options.Create(new AwsSqsTransportOptions
        {
            WaitTimeSeconds = 12,
            MaxNumberOfMessages = 7
        });

        ReceiveMessageRequest? capturedReceiveRequest = null;
        var tcsReceived = new TaskCompletionSource<bool>();

        client.ReceiveMessageAsync(Arg.Do<ReceiveMessageRequest>(r =>
        {
            capturedReceiveRequest = r;
            tcsReceived.TrySetResult(true);
        }), Arg.Any<CancellationToken>())
        .Returns(async callInfo =>
        {
            var token = callInfo.Arg<CancellationToken>();
            var tcs = new TaskCompletionSource<ReceiveMessageResponse>();
            token.Register(() => tcs.TrySetCanceled());
            return await tcs.Task;
        });

        using var transport = new AwsSqsMessageTransport(options: options, sqsClient: client);

        var subResult = await transport.SubscribeAsync("https://sqs.queue", (_, _, _) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions(), cts.Token);

        await Task.WhenAny(tcsReceived.Task, Task.Delay(2000));
        cts.Cancel();

        subResult.IsSuccess.Should().BeTrue();
        capturedReceiveRequest.Should().NotBeNull();
        capturedReceiveRequest!.QueueUrl.Should().Be("https://sqs.queue");
        capturedReceiveRequest.WaitTimeSeconds.Should().Be(12);
        capturedReceiveRequest.MaxNumberOfMessages.Should().Be(7);
        capturedReceiveRequest.MessageAttributeNames.Should().ContainSingle().Which.Should().Be("All");
    }

    [Fact]
    public async Task SubscribeAsync_MaxNumberOfMessagesExceeding10_ClampedTo10()
    {
        var client = Substitute.For<IAmazonSQS>();
        using var cts = new CancellationTokenSource();

        var options = Options.Create(new AwsSqsTransportOptions
        {
            WaitTimeSeconds = 15,
            MaxNumberOfMessages = 25 // Exceeds AWS SQS max of 10
        });

        ReceiveMessageRequest? capturedReceiveRequest = null;
        var tcsReceived = new TaskCompletionSource<bool>();

        client.ReceiveMessageAsync(Arg.Do<ReceiveMessageRequest>(r =>
        {
            capturedReceiveRequest = r;
            tcsReceived.TrySetResult(true);
        }), Arg.Any<CancellationToken>())
        .Returns(async callInfo =>
        {
            var token = callInfo.Arg<CancellationToken>();
            var tcs = new TaskCompletionSource<ReceiveMessageResponse>();
            token.Register(() => tcs.TrySetCanceled());
            return await tcs.Task;
        });

        using var transport = new AwsSqsMessageTransport(options: options, sqsClient: client);

        var subResult = await transport.SubscribeAsync("https://sqs.queue", (_, _, _) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions(), cts.Token);

        await Task.WhenAny(tcsReceived.Task, Task.Delay(2000));
        cts.Cancel();

        subResult.IsSuccess.Should().BeTrue();
        capturedReceiveRequest.Should().NotBeNull();
        capturedReceiveRequest!.MaxNumberOfMessages.Should().Be(10);
    }

    [Fact]
    public async Task SubscribeAsync_ProcessesMessages_AndDeletesOnAck()
    {
        var client = Substitute.For<IAmazonSQS>();
        using var cts = new CancellationTokenSource();

        var originalPayload = Encoding.UTF8.GetBytes("message body");
        var base64Payload = Convert.ToBase64String(originalPayload);

        var message = new Message
        {
            MessageId = "sqs-msg-1",
            ReceiptHandle = "receipt-1",
            Body = base64Payload,
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["message-id"] = new() { DataType = "String", StringValue = "custom-id" },
                ["message-type"] = new() { DataType = "String", StringValue = "UserRegistered" },
                ["correlation-id"] = new() { DataType = "String", StringValue = "corr-1" },
                ["causation-id"] = new() { DataType = "String", StringValue = "cause-1" },
                ["traceparent"] = new() { DataType = "String", StringValue = "00-trace-01" },
                ["tenant-id"] = new() { DataType = "String", StringValue = "tenant-1" },
                ["partition-key"] = new() { DataType = "String", StringValue = "pk-1" },
                ["content-type"] = new() { DataType = "String", StringValue = "application/custom-json" },
                ["schema-version"] = new() { DataType = "String", StringValue = "4" },
                ["x-custom-attr"] = new() { DataType = "String", StringValue = "attr-val" }
            }
        };

        var callCount = 0;
        var tcsDeleted = new TaskCompletionSource<bool>();

        client.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
              .Returns(async callInfo =>
              {
                  callCount++;
                  if (callCount == 1)
                  {
                      return new ReceiveMessageResponse
                      {
                          Messages = new List<Message> { message }
                      };
                  }

                  var token = callInfo.Arg<CancellationToken>();
                  var tcs = new TaskCompletionSource<ReceiveMessageResponse>();
                  token.Register(() => tcs.TrySetCanceled());
                  return await tcs.Task;
              });

        client.DeleteMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(callInfo =>
              {
                  tcsDeleted.TrySetResult(true);
                  return Task.FromResult(new DeleteMessageResponse());
              });

        using var transport = new AwsSqsMessageTransport(sqsClient: client);
        ReadOnlyMemory<byte> capturedPayload = default;
        TransportMessageMetadata? capturedMeta = null;

        var subResult = await transport.SubscribeAsync("https://sqs.us-east-1.amazonaws.com/123/my-queue", (p, m, ct) =>
        {
            capturedPayload = p;
            capturedMeta = m;
            return ValueTask.FromResult(TransportAckResult.Ack);
        }, new TransportSubscriptionOptions(), cts.Token);

        await Task.WhenAny(tcsDeleted.Task, Task.Delay(3000));
        cts.Cancel();

        subResult.IsSuccess.Should().BeTrue();
        tcsDeleted.Task.IsCompletedSuccessfully.Should().BeTrue();
        capturedPayload.ToArray().Should().BeEquivalentTo(originalPayload);
        capturedMeta.Should().NotBeNull();
        capturedMeta!.MessageId.Should().Be("custom-id");
        capturedMeta.MessageType.Should().Be("UserRegistered");
        capturedMeta.CorrelationId.Should().Be("corr-1");
        capturedMeta.CausationId.Should().Be("cause-1");
        capturedMeta.TraceParent.Should().Be("00-trace-01");
        capturedMeta.TenantId.Should().Be("tenant-1");
        capturedMeta.PartitionKey.Should().Be("pk-1");
        capturedMeta.ContentType.Should().Be("application/custom-json");
        capturedMeta.SchemaVersion.Should().Be(4);
        capturedMeta.Headers!["x-custom-attr"].Should().Be("attr-val");

        await client.Received(1).DeleteMessageAsync(
            "https://sqs.us-east-1.amazonaws.com/123/my-queue",
            "receipt-1",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_WhenNackOrDeadLetter_DoesNotDeleteMessage()
    {
        var client = Substitute.For<IAmazonSQS>();
        using var cts = new CancellationTokenSource();

        var message = new Message
        {
            MessageId = "sqs-msg-2",
            ReceiptHandle = "receipt-2",
            Body = "plain-utf8-not-base64",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>()
        };

        var callCount = 0;
        client.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
              .Returns(async callInfo =>
              {
                  callCount++;
                  if (callCount == 1)
                  {
                      return new ReceiveMessageResponse
                      {
                          Messages = new List<Message> { message }
                      };
                  }

                  var token = callInfo.Arg<CancellationToken>();
                  var tcs = new TaskCompletionSource<ReceiveMessageResponse>();
                  token.Register(() => tcs.TrySetCanceled());
                  return await tcs.Task;
              });

        using var transport = new AwsSqsMessageTransport(sqsClient: client);
        var tcsHandled = new TaskCompletionSource<bool>();

        await transport.SubscribeAsync("queue", (p, m, ct) =>
        {
            tcsHandled.TrySetResult(true);
            return ValueTask.FromResult(TransportAckResult.NackRequeue);
        }, new TransportSubscriptionOptions(), cts.Token);

        await Task.WhenAny(tcsHandled.Task, Task.Delay(2000));
        cts.Cancel();

        tcsHandled.Task.IsCompletedSuccessfully.Should().BeTrue();
        await client.DidNotReceive().DeleteMessageAsync(
            Arg.Any<string>(),
            "receipt-2",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_FallbackMetadata_EmptyAttributesAndNullId_GeneratesGuidsAndDefaults()
    {
        var client = Substitute.For<IAmazonSQS>();
        using var cts = new CancellationTokenSource();

        var message = new Message
        {
            MessageId = null,
            ReceiptHandle = "receipt-empty",
            Body = "raw-string-body",
            MessageAttributes = null!
        };

        var callCount = 0;
        client.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
              .Returns(async callInfo =>
              {
                  callCount++;
                  if (callCount == 1)
                  {
                      return new ReceiveMessageResponse
                      {
                          Messages = new List<Message> { message }
                      };
                  }

                  var token = callInfo.Arg<CancellationToken>();
                  var tcs = new TaskCompletionSource<ReceiveMessageResponse>();
                  token.Register(() => tcs.TrySetCanceled());
                  return await tcs.Task;
              });

        using var transport = new AwsSqsMessageTransport(sqsClient: client);
        TransportMessageMetadata? capturedMeta = null;
        var tcsHandled = new TaskCompletionSource<bool>();

        await transport.SubscribeAsync("queue", (p, m, ct) =>
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
        capturedMeta.MessageType.Should().Be("AwsSqsMessage");
        capturedMeta.ContentType.Should().Be("application/json");
        capturedMeta.SchemaVersion.Should().Be(1);
    }

    [Fact]
    public async Task SubscribeAsync_WithNoMessageIdInAttributes_UsesSqsMessageId()
    {
        var client = Substitute.For<IAmazonSQS>();
        using var cts = new CancellationTokenSource();

        var message = new Message
        {
            MessageId = "sqs-raw-msg-456",
            ReceiptHandle = "receipt-456",
            Body = "raw-body",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["tenant-id"] = new() { DataType = "String", StringValue = "tenant-xyz" }
            }
        };

        var callCount = 0;
        client.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
              .Returns(async callInfo =>
              {
                  callCount++;
                  if (callCount == 1)
                  {
                      return new ReceiveMessageResponse
                      {
                          Messages = new List<Message> { message }
                      };
                  }

                  var token = callInfo.Arg<CancellationToken>();
                  var tcs = new TaskCompletionSource<ReceiveMessageResponse>();
                  token.Register(() => tcs.TrySetCanceled());
                  return await tcs.Task;
              });

        using var transport = new AwsSqsMessageTransport(sqsClient: client);
        TransportMessageMetadata? capturedMeta = null;
        var tcsHandled = new TaskCompletionSource<bool>();

        await transport.SubscribeAsync("queue", (p, m, ct) =>
        {
            capturedMeta = m;
            tcsHandled.TrySetResult(true);
            return ValueTask.FromResult(TransportAckResult.Ack);
        }, new TransportSubscriptionOptions(), cts.Token);

        await Task.WhenAny(tcsHandled.Task, Task.Delay(2000));
        cts.Cancel();

        tcsHandled.Task.IsCompletedSuccessfully.Should().BeTrue();
        capturedMeta.Should().NotBeNull();
        capturedMeta!.MessageId.Should().Be("sqs-raw-msg-456");
        capturedMeta.TenantId.Should().Be("tenant-xyz");
    }

    [Fact]
    public async Task SubscribeAsync_PollingLoopThrowsException_LogsErrorAndDelaysBeforeRetry()
    {
        var client = Substitute.For<IAmazonSQS>();
        var logger = Substitute.For<ILogger<AwsSqsMessageTransport>>();
        using var cts = new CancellationTokenSource();

        var callCount = 0;
        var tcsCall2 = new TaskCompletionSource<bool>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long delayMs = 0;

        client.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
              .Returns(async callInfo =>
              {
                  callCount++;
                  if (callCount == 1)
                  {
                      sw.Restart();
                      throw new AmazonSQSException("Transient queue error");
                  }

                  delayMs = sw.ElapsedMilliseconds;
                  tcsCall2.TrySetResult(true);
                  var token = callInfo.Arg<CancellationToken>();
                  var tcs = new TaskCompletionSource<ReceiveMessageResponse>();
                  token.Register(() => tcs.TrySetCanceled());
                  return await tcs.Task;
              });

        using var transport = new AwsSqsMessageTransport(sqsClient: client, logger: logger);

        await transport.SubscribeAsync("error-queue", (p, m, ct) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions(), cts.Token);

        await Task.WhenAny(tcsCall2.Task, Task.Delay(3000));
        cts.Cancel();

        tcsCall2.Task.IsCompletedSuccessfully.Should().BeTrue();
        delayMs.Should().BeGreaterThanOrEqualTo(800);

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Error polling AWS SQS queue")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    #endregion

    #region Dispose & DisposeAsync

    [Fact]
    public async Task Dispose_WithActiveSubscriptions_CancelsAndDisposesAllSubscriptionsAndClient()
    {
        var client = Substitute.For<IAmazonSQS>();
        var transport = new AwsSqsMessageTransport(sqsClient: client);

        client.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
              .Returns(async callInfo =>
              {
                  var token = callInfo.Arg<CancellationToken>();
                  var tcs = new TaskCompletionSource<ReceiveMessageResponse>();
                  token.Register(() => tcs.TrySetCanceled());
                  return await tcs.Task;
              });

        await transport.SubscribeAsync("q1", (p, m, ct) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());
        await transport.SubscribeAsync("q2", (p, m, ct) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());

        transport.Dispose();
        transport.Dispose(); // Idempotent call

        client.Received(1).Dispose();

        Func<Task> act = async () => await transport.PublishRawAsync("https://sqs.us-east-1.amazonaws.com/123/q1", new byte[] { 1 }, TransportMessageMetadata.Create("test"));
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task DisposeAsync_CallsDispose_IsIdempotent()
    {
        var client = Substitute.For<IAmazonSQS>();
        var transport = new AwsSqsMessageTransport(sqsClient: client);

        await transport.DisposeAsync();
        await transport.DisposeAsync();

        client.Received(1).Dispose();
    }

    [Fact]
    public async Task DisposeAsync_WithActiveSubscriptions_CancelsAndDisposesAllResources()
    {
        var client = Substitute.For<IAmazonSQS>();
        var tcs = new TaskCompletionSource<ReceiveMessageResponse>();
        client.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var ct = callInfo.Arg<CancellationToken>();
                using (ct.Register(() => tcs.TrySetCanceled()))
                {
                    return await tcs.Task;
                }
            });

        var transport = new AwsSqsMessageTransport(sqsClient: client);
        await transport.SubscribeAsync("https://sqs.queue1", (_, _, _) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());
        await transport.SubscribeAsync("https://sqs.queue2", (_, _, _) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());

        var subField = typeof(AwsSqsMessageTransport).GetField("_subscriptions", BindingFlags.NonPublic | BindingFlags.Instance);
        var subs = (System.Collections.IDictionary)subField!.GetValue(transport)!;
        subs.Count.Should().Be(2);

        await transport.DisposeAsync();

        subs.Count.Should().Be(0);
        var bgField = typeof(AwsSqsMessageTransport).GetField("_backgroundTasks", BindingFlags.NonPublic | BindingFlags.Instance);
        var bgTasks = (System.Collections.IDictionary)bgField!.GetValue(transport)!;
        bgTasks.Count.Should().Be(0);
        client.Received(1).Dispose();
    }

    [Fact]
    public async Task Dispose_WithActiveSubscriptions_CancelsCts()
    {
        var client = Substitute.For<IAmazonSQS>();
        var tcs = new TaskCompletionSource<ReceiveMessageResponse>();
        client.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var ct = callInfo.Arg<CancellationToken>();
                using (ct.Register(() => tcs.TrySetCanceled()))
                {
                    return await tcs.Task;
                }
            });

        var transport = new AwsSqsMessageTransport(sqsClient: client);
        await transport.SubscribeAsync("https://sqs.queue1", (_, _, _) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());

        var subField = typeof(AwsSqsMessageTransport).GetField("_subscriptions", BindingFlags.NonPublic | BindingFlags.Instance);
        var subs = (System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource>)subField!.GetValue(transport)!;
        var cts = subs.Values.First();

        transport.Dispose();

        cts.IsCancellationRequested.Should().BeTrue();
        client.Received(1).Dispose();
    }

    [Fact]
    public async Task SubscribeAsync_WhenResponseMessagesEmpty_DoesNotInvokeHandler()
    {
        var client = Substitute.For<IAmazonSQS>();
        using var cts = new CancellationTokenSource();
        int callCount = 0;
        int handlerInvocations = 0;

        client.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                {
                    return Task.FromResult(new ReceiveMessageResponse { Messages = new List<Message>() });
                }
                cts.Cancel();
                throw new OperationCanceledException();
            });

        var transport = new AwsSqsMessageTransport(sqsClient: client);
        await transport.SubscribeAsync("https://sqs.queue", (p, m, ct) =>
        {
            Interlocked.Increment(ref handlerInvocations);
            return ValueTask.FromResult(TransportAckResult.Ack);
        }, new TransportSubscriptionOptions(), cts.Token);

        await Task.Delay(50);
        handlerInvocations.Should().Be(0);
    }

    [Fact]
    public async Task SubscribeAsync_WithMaxConcurrencyGreaterThanOne_ProcessesMessagesConcurrently()
    {
        var client = Substitute.For<IAmazonSQS>();
        var transport = new AwsSqsMessageTransport(sqsClient: client);

        var messages = new List<Message>
        {
            new() { MessageId = "m1", Body = "body1", ReceiptHandle = "r1" },
            new() { MessageId = "m2", Body = "body2", ReceiptHandle = "r2" },
            new() { MessageId = "m3", Body = "body3", ReceiptHandle = "r3" },
            new() { MessageId = "m4", Body = "body4", ReceiptHandle = "r4" },
        };

        var response = new ReceiveMessageResponse { Messages = messages };
        using var cts = new CancellationTokenSource();
        int callCount = 0;

        client.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
              .Returns(callInfo =>
              {
                  if (Interlocked.Increment(ref callCount) == 1)
                  {
                      return Task.FromResult(response);
                  }

                  cts.Cancel();
                  throw new OperationCanceledException();
              });

        int inFlight = 0;
        int maxInFlight = 0;
        var barrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int completedCount = 0;

        int deleteCount = 0;
        var deletesDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.DeleteMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(callInfo =>
              {
                  if (Interlocked.Increment(ref deleteCount) == 4)
                  {
                      deletesDone.TrySetResult(true);
                  }
                  return Task.FromResult(new DeleteMessageResponse());
              });

        var subOptions = new TransportSubscriptionOptions { MaxConcurrency = 4 };

        await transport.SubscribeAsync("https://sqs.queue", async (p, m, ct) =>
        {
            int current = Interlocked.Increment(ref inFlight);
            lock (messages)
            {
                if (current > maxInFlight) maxInFlight = current;
            }

            if (current >= 2)
            {
                barrier.TrySetResult(true);
            }

            await Task.WhenAny(barrier.Task, Task.Delay(500, ct));

            Interlocked.Decrement(ref inFlight);
            if (Interlocked.Increment(ref completedCount) == 4)
            {
                allDone.TrySetResult(true);
            }

            return TransportAckResult.Ack;
        }, subOptions, cts.Token);

        await allDone.Task;
        await deletesDone.Task;
        maxInFlight.Should().BeGreaterThan(1, "messages in the batch must be processed concurrently when MaxConcurrency > 1");
        completedCount.Should().Be(4);
        deleteCount.Should().Be(4);
    }

    #endregion
}

