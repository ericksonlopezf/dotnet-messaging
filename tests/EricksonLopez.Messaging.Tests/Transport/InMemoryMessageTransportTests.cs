// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;

namespace EricksonLopez.Messaging.Tests.Transport;

using System.Text;
using System.Threading.Channels;
using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Tests.Common;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Messaging.Transport.InMemory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

[Trait("Category", "Transport")]
public class InMemoryMessageTransportTests
{

    [Fact]
    public void Constructor_DefaultParameters_InitializesCorrectly()
    {
        // Act
        var transport = new InMemoryMessageTransport();

        // Assert
        transport.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithOptionsAndLogger_InitializesCorrectly()
    {
        // Arrange
        var options = Options.Create(new InMemoryTransportOptions { ChannelCapacity = 50 });
        var logger = Substitute.For<ILogger<InMemoryMessageTransport>>();

        // Act
        var transport = new InMemoryMessageTransport(options, logger);

        // Assert
        transport.Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PublishRawAsync_InvalidDestination_ThrowsArgumentException(string? invalidDestination)
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        var payload = Encoding.UTF8.GetBytes("payload");
        var metadata = TestMessageContextFactory.CreateMetadata();

        // Act
        Func<Task> act = async () => await transport.PublishRawAsync(invalidDestination!, payload, metadata);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PublishRawAsync_NullMetadata_ThrowsArgumentNullException()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        var payload = Encoding.UTF8.GetBytes("payload");

        // Act
        Func<Task> act = async () => await transport.PublishRawAsync("topic", payload, null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishRawAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        await transport.DisposeAsync();
        var payload = Encoding.UTF8.GetBytes("payload");
        var metadata = TestMessageContextFactory.CreateMetadata();

        // Act
        Func<Task> act = async () => await transport.PublishRawAsync("topic", payload, metadata);

        // Assert
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SubscribeAsync_InvalidDestination_ThrowsArgumentException(string? invalidDestination)
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        var options = new TransportSubscriptionOptions();

        // Act
        Func<Task> act = async () => await transport.SubscribeAsync(
            invalidDestination!,
            (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            options);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SubscribeAsync_NullHandler_ThrowsArgumentNullException()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        var options = new TransportSubscriptionOptions();

        // Act
        Func<Task> act = async () => await transport.SubscribeAsync("topic", null!, options);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SubscribeAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        await transport.DisposeAsync();
        var options = new TransportSubscriptionOptions();

        // Act
        Func<Task> act = async () => await transport.SubscribeAsync(
            "topic",
            (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            options);

        // Assert
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task PublishRawAsync_NoSubscribers_LogsDebugAndReturnsSuccess()
    {
        // Arrange
        var logger = Substitute.For<ILogger<InMemoryMessageTransport>>();
        var transport = new InMemoryMessageTransport(logger: logger);
        var payload = Encoding.UTF8.GetBytes("payload");
        var metadata = TestMessageContextFactory.CreateMetadata();

        // Act
        var result = await transport.PublishRawAsync("unrouted.topic", payload, metadata);

        // Assert
        result.IsSuccess.Should().BeTrue();
        logger.Received(1).Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("No active subscribers for destination 'unrouted.topic'")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task PublishAndSubscribe_SingleSubscriber_ReceivesMessageSuccessfully()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        var receivedTcs = new TaskCompletionSource<(string Body, TransportMessageMetadata Meta)>(TaskCreationOptions.RunContinuationsAsynchronously);

        var subscribeResult = await transport.SubscribeAsync(
            "orders.created",
            (body, meta, ct) =>
            {
                receivedTcs.TrySetResult((Encoding.UTF8.GetString(body.Span), meta));
                return ValueTask.FromResult(TransportAckResult.Ack);
            },
            new TransportSubscriptionOptions { MaxConcurrency = 1 });

        subscribeResult.IsSuccess.Should().BeTrue();

        var expectedPayload = Encoding.UTF8.GetBytes("{\"orderId\":123}");
        var expectedMetadata = TestMessageContextFactory.CreateMetadata("OrderCreated", "CORR-999");

        // Act
        var publishResult = await transport.PublishRawAsync("orders.created", expectedPayload, expectedMetadata);

        // Assert
        publishResult.IsSuccess.Should().BeTrue();

        var (receivedBody, receivedMeta) = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        receivedBody.Should().Be("{\"orderId\":123}");
        receivedMeta.MessageId.Should().Be(expectedMetadata.MessageId);
        receivedMeta.CorrelationId.Should().Be("CORR-999");

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task PublishAndSubscribe_MultipleSubscribers_BroadcastsToAll()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        var sub1Tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sub2Tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync(
            "broadcast.topic",
            (body, meta, ct) =>
            {
                sub1Tcs.TrySetResult(true);
                return ValueTask.FromResult(TransportAckResult.Ack);
            },
            new TransportSubscriptionOptions { MaxConcurrency = 1 });

        await transport.SubscribeAsync(
            "broadcast.topic",
            (body, meta, ct) =>
            {
                sub2Tcs.TrySetResult(true);
                return ValueTask.FromResult(TransportAckResult.Ack);
            },
            new TransportSubscriptionOptions { MaxConcurrency = 2 });

        var payload = Encoding.UTF8.GetBytes("broadcast-message");
        var metadata = TestMessageContextFactory.CreateMetadata();

        // Act
        var publishResult = await transport.PublishRawAsync("broadcast.topic", payload, metadata);

        // Assert
        publishResult.IsSuccess.Should().BeTrue();
        (await sub1Tcs.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        (await sub2Tcs.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task SubscribeAsync_NackRequeue_RequeuesMessageAndRetries()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        int attempts = 0;
        var retryTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync(
            "requeue.topic",
            (body, meta, ct) =>
            {
                int current = Interlocked.Increment(ref attempts);
                if (current == 1)
                {
                    return ValueTask.FromResult(TransportAckResult.NackRequeue);
                }

                retryTcs.TrySetResult(current);
                return ValueTask.FromResult(TransportAckResult.Ack);
            },
            new TransportSubscriptionOptions { MaxConcurrency = 1 });

        var payload = Encoding.UTF8.GetBytes("requeue-payload");
        var metadata = TestMessageContextFactory.CreateMetadata();

        // Act
        await transport.PublishRawAsync("requeue.topic", payload, metadata);

        // Assert
        var resultAttempts = await retryTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        resultAttempts.Should().Be(2);

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task SubscribeAsync_DeadLetter_DoesNotRequeue()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        int attempts = 0;
        var dlqTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync(
            "dlq.topic",
            (body, meta, ct) =>
            {
                int current = Interlocked.Increment(ref attempts);
                dlqTcs.TrySetResult(current);
                return ValueTask.FromResult(TransportAckResult.DeadLetter);
            },
            new TransportSubscriptionOptions { MaxConcurrency = 1 });

        var payload = Encoding.UTF8.GetBytes("dlq-payload");
        var metadata = TestMessageContextFactory.CreateMetadata();

        // Act
        await transport.PublishRawAsync("dlq.topic", payload, metadata);

        // Assert
        var resultAttempts = await dlqTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        resultAttempts.Should().Be(1);

        await Task.Delay(50);
        attempts.Should().Be(1);

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task SubscribeAsync_HandlerThrowsException_LogsErrorAndRecovers()
    {
        // Arrange
        var logger = Substitute.For<ILogger<InMemoryMessageTransport>>();
        var transport = new InMemoryMessageTransport(logger: logger);
        int attempts = 0;
        var secondMsgTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync(
            "error.topic",
            (body, meta, ct) =>
            {
                int current = Interlocked.Increment(ref attempts);
                if (current == 1)
                {
                    throw new InvalidOperationException("Handler exploded");
                }

                secondMsgTcs.TrySetResult(true);
                return ValueTask.FromResult(TransportAckResult.Ack);
            },
            new TransportSubscriptionOptions { MaxConcurrency = 1 });

        // Act - publish first message which throws
        var meta1 = TestMessageContextFactory.CreateMetadata();
        await transport.PublishRawAsync("error.topic", Encoding.UTF8.GetBytes("msg1"), meta1);

        // Wait a small moment for error handling
        await Task.Delay(100);

        // Publish second message which should be processed normally
        await transport.PublishRawAsync("error.topic", Encoding.UTF8.GetBytes("msg2"), TestMessageContextFactory.CreateMetadata());

        // Assert
        (await secondMsgTcs.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains($"Unexpected error processing in-memory message '{meta1.MessageId}'")),
            Arg.Is<Exception>(ex => ex is InvalidOperationException && ex.Message == "Handler exploded"),
            Arg.Any<Func<object, Exception?, string>>());

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task PublishRawAsync_ChannelSaturation_AwaitsWriteAsyncAndEnforcesBackpressure()
    {
        // Arrange - set tiny capacity of 1
        var options = Options.Create(new InMemoryTransportOptions
        {
            ChannelCapacity = 1,
            FullMode = BoundedChannelFullMode.Wait
        });
        var transport = new InMemoryMessageTransport(options);

        var processTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedMessages = new List<string>();
        var allReceivedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync(
            "backpressure.topic",
            async (body, meta, ct) =>
            {
                var text = Encoding.UTF8.GetString(body.Span);
                lock (receivedMessages)
                {
                    receivedMessages.Add(text);
                }
                if (text == "m1")
                {
                    await processTcs.Task;
                }
                else if (text == "m3")
                {
                    allReceivedTcs.TrySetResult(true);
                }
                return TransportAckResult.Ack;
            },
            new TransportSubscriptionOptions { MaxConcurrency = 1 });

        // Act - write 3 messages (m1 processed, m2 fills capacity-1 channel, m3 awaits WriteAsync)
        var p1 = await transport.PublishRawAsync("backpressure.topic", Encoding.UTF8.GetBytes("m1"), TestMessageContextFactory.CreateMetadata());
        var p2 = await transport.PublishRawAsync("backpressure.topic", Encoding.UTF8.GetBytes("m2"), TestMessageContextFactory.CreateMetadata());

        // Publish m3 in background task because it will await WriteAsync until m1 is released
        var p3Task = Task.Run(async () => await transport.PublishRawAsync("backpressure.topic", Encoding.UTF8.GetBytes("m3"), TestMessageContextFactory.CreateMetadata()));

        p1.IsSuccess.Should().BeTrue();
        p2.IsSuccess.Should().BeTrue();

        // Release processing of m1
        processTcs.TrySetResult(true);

        var p3Result = await p3Task;
        p3Result.IsSuccess.Should().BeTrue();

        // Verify m3 was received after backpressure cleared
        await allReceivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        lock (receivedMessages)
        {
            receivedMessages.Should().ContainInOrder("m1", "m2", "m3");
        }

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task SubscribeAsync_WithOptionsFullModeDropOldest_DropsOldestWhenSaturated()
    {
        // Arrange
        var options = Options.Create(new InMemoryTransportOptions
        {
            ChannelCapacity = 1,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        var transport = new InMemoryMessageTransport(options);

        var m1StartedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockM1Tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedList = new List<string>();
        var completeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync(
            "drop.topic",
            async (body, meta, ct) =>
            {
                var text = Encoding.UTF8.GetString(body.Span);
                lock (receivedList)
                {
                    receivedList.Add(text);
                }

                if (text == "m1")
                {
                    m1StartedTcs.TrySetResult(true);
                    await blockM1Tcs.Task;
                }
                else if (text == "m3")
                {
                    completeTcs.TrySetResult(true);
                }

                return TransportAckResult.Ack;
            },
            new TransportSubscriptionOptions { MaxConcurrency = 1 });

        // Act - Publish m1 and ensure it starts processing and blocks the single consumer
        await transport.PublishRawAsync("drop.topic", Encoding.UTF8.GetBytes("m1"), TestMessageContextFactory.CreateMetadata());
        await m1StartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Publish m2 (fills channel capacity 1), then m3 (drops m2 due to DropOldest, replaces buffer with m3)
        await transport.PublishRawAsync("drop.topic", Encoding.UTF8.GetBytes("m2"), TestMessageContextFactory.CreateMetadata());
        await transport.PublishRawAsync("drop.topic", Encoding.UTF8.GetBytes("m3"), TestMessageContextFactory.CreateMetadata());

        // Unblock m1 so m1 finishes, and consumer consumes m3 (m2 was dropped)
        blockM1Tcs.TrySetResult(true);

        await completeTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        lock (receivedList)
        {
            receivedList.Should().Equal("m1", "m3");
        }

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task SubscribeAsync_MaxConcurrency1_EnforcesSequentialProcessing()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        var inFlight = 0;
        var maxObserved = 0;
        var processed = 0;
        var doneTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync(
            "seq.topic",
            async (body, meta, ct) =>
            {
                var current = Interlocked.Increment(ref inFlight);
                lock (doneTcs)
                {
                    if (current > maxObserved) maxObserved = current;
                }

                await Task.Delay(30, ct);
                Interlocked.Decrement(ref inFlight);

                if (Interlocked.Increment(ref processed) == 2)
                {
                    doneTcs.TrySetResult(true);
                }

                return TransportAckResult.Ack;
            },
            new TransportSubscriptionOptions { MaxConcurrency = 1 });

        // Act
        await transport.PublishRawAsync("seq.topic", Encoding.UTF8.GetBytes("1"), TestMessageContextFactory.CreateMetadata());
        await transport.PublishRawAsync("seq.topic", Encoding.UTF8.GetBytes("2"), TestMessageContextFactory.CreateMetadata());

        await doneTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert - strictly sequential, maxObserved in flight is 1
        maxObserved.Should().Be(1);

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task SubscribeAsync_ParallelConcurrency_ProcessesConcurrently()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        var inFlight = 0;
        var maxObservedInFlight = 0;
        var countTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var processed = 0;

        await transport.SubscribeAsync(
            "parallel.topic",
            async (body, meta, ct) =>
            {
                var cur = Interlocked.Increment(ref inFlight);
                lock (countTcs)
                {
                    if (cur > maxObservedInFlight) maxObservedInFlight = cur;
                }
                await Task.Delay(50, ct);
                Interlocked.Decrement(ref inFlight);

                if (Interlocked.Increment(ref processed) == 4)
                {
                    countTcs.TrySetResult(true);
                }

                return TransportAckResult.Ack;
            },
            new TransportSubscriptionOptions { MaxConcurrency = 4 });

        // Act - publish 4 messages
        for (int i = 0; i < 4; i++)
        {
            await transport.PublishRawAsync("parallel.topic", Encoding.UTF8.GetBytes($"p{i}"), TestMessageContextFactory.CreateMetadata());
        }

        await countTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert that concurrency > 1 was achieved
        maxObservedInFlight.Should().BeGreaterThan(1);

        await transport.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_WithActiveSubscription_CompletesChannelsAndCancelsLoops()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        var enteredTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loopCancelledTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync(
            "dispose.topic",
            async (body, meta, ct) =>
            {
                enteredTcs.TrySetResult(true);
                try
                {
                    await Task.Delay(10000, ct);
                }
                catch (OperationCanceledException)
                {
                    loopCancelledTcs.TrySetResult(true);
                }
                return TransportAckResult.Ack;
            },
            new TransportSubscriptionOptions());

        // Publish message so handler is waiting in Task.Delay with ct
        await transport.PublishRawAsync("dispose.topic", Encoding.UTF8.GetBytes("msg"), TestMessageContextFactory.CreateMetadata());
        await enteredTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act - dispose transport
        await transport.DisposeAsync();

        // Assert - cancellation token was triggered
        var cancelled = await loopCancelledTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancelled.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_Idempotent_CanBeCalledMultipleTimes()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        await transport.SubscribeAsync(
            "topic",
            (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            new TransportSubscriptionOptions());

        // Act
        await transport.DisposeAsync();
        await transport.DisposeAsync(); // Second call must not throw

        // Assert
        Func<Task> act = async () => await transport.PublishRawAsync("topic", Encoding.UTF8.GetBytes("1"), TestMessageContextFactory.CreateMetadata());
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task DeferRawAsync_ZeroOrNegativeDelay_PublishesImmediately()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        var receivedTcs = new TaskCompletionSource<bool>();

        await transport.SubscribeAsync(
            "defer.immediate",
            (payload, metadata, ct) =>
            {
                receivedTcs.TrySetResult(true);
                return ValueTask.FromResult(TransportAckResult.Ack);
            },
            new TransportSubscriptionOptions());

        // Act
        var result = await transport.DeferRawAsync(
            "defer.immediate",
            Encoding.UTF8.GetBytes("hello"),
            TestMessageContextFactory.CreateMetadata(),
            TimeSpan.Zero);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var received = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        received.Should().BeTrue();
    }

    [Fact]
    public async Task DeferRawAsync_WithPositiveDelay_DeliversAfterDelay()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        var receivedTcs = new TaskCompletionSource<DateTime>();

        await transport.SubscribeAsync(
            "defer.delayed",
            (payload, metadata, ct) =>
            {
                receivedTcs.TrySetResult(DateTime.UtcNow);
                return ValueTask.FromResult(TransportAckResult.Ack);
            },
            new TransportSubscriptionOptions());

        var startTime = DateTime.UtcNow;

        // Act
        var result = await transport.DeferRawAsync(
            "defer.delayed",
            Encoding.UTF8.GetBytes("hello"),
            TestMessageContextFactory.CreateMetadata(),
            TimeSpan.FromMilliseconds(200));

        // Assert
        result.IsSuccess.Should().BeTrue();
        var receivedTime = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        (receivedTime - startTime).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public async Task DeferRawAsync_DisposedTransport_ThrowsObjectDisposedException()
    {
        var transport = new InMemoryMessageTransport();
        await transport.DisposeAsync();

        Func<Task> act = async () => await transport.DeferRawAsync(
            "topic",
            Encoding.UTF8.GetBytes("1"),
            TestMessageContextFactory.CreateMetadata(),
            TimeSpan.FromSeconds(1));

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_Synchronous_MarksDisposedAndSubsequentCallsThrowObjectDisposedException()
    {
        var transport = new InMemoryMessageTransport();
        transport.Dispose();

        Func<Task> publishAct = async () => await transport.PublishRawAsync("topic", Encoding.UTF8.GetBytes("1"), TestMessageContextFactory.CreateMetadata());
        publishAct.Should().ThrowAsync<ObjectDisposedException>();

        Func<Task> subscribeAct = async () => await transport.SubscribeAsync("topic", (b, m, ct) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());
        subscribeAct.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_Idempotency_MultipleCallsDoNotThrow()
    {
        var transport = new InMemoryMessageTransport();
        Action act1 = () => transport.Dispose();
        Action act2 = () => transport.Dispose();

        act1.Should().NotThrow();
        act2.Should().NotThrow();
    }

    [Fact]
    public async Task DisposeAsync_Idempotency_MultipleCallsDoNotThrow()
    {
        var transport = new InMemoryMessageTransport();
        Func<Task> act1 = async () => await transport.DisposeAsync();
        Func<Task> act2 = async () => await transport.DisposeAsync();

        await act1.Should().NotThrowAsync();
        await act2.Should().NotThrowAsync();
    }

    #region DeferRawAsync Tests

    [Fact]
    public async Task DeferRawAsync_WhenDelayIsZero_DeliversImmediately()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        var destination = "orders";
        var payload = new byte[] { 1, 2, 3 };
        var metadata = TestMessageContextFactory.CreateMetadata();
        var received = new System.Collections.Concurrent.ConcurrentBag<ReadOnlyMemory<byte>>();

        var subscribeResult = await transport.SubscribeAsync(
            destination,
            (p, m, ct) =>
            {
                received.Add(p.ToArray());
                return ValueTask.FromResult(TransportAckResult.Ack);
            },
            new TransportSubscriptionOptions { MaxConcurrency = 1 });

        // Act — zero delay should call PublishRawAsync directly
        var result = await transport.DeferRawAsync(destination, payload, metadata, TimeSpan.Zero);

        // Allow brief processing
        await Task.Delay(100);

        // Assert
        result.IsSuccess.Should().BeTrue();
        subscribeResult.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeferRawAsync_WhenDelayIsNegative_DeliversImmediately()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        var destination = "payments";
        var payload = new byte[] { 9, 8, 7 };
        var metadata = TestMessageContextFactory.CreateMetadata();

        // Act — negative delay should also call PublishRawAsync directly
        var result = await transport.DeferRawAsync(destination, payload, metadata, TimeSpan.FromSeconds(-5));

        // Assert — success even without subscribers (unrouted scenario)
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeferRawAsync_WhenDelayIsPositive_ReturnsSuccessWithoutWaiting()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        var destination = "notifications";
        var payload = new byte[] { 1 };
        var metadata = TestMessageContextFactory.CreateMetadata();

        // Act — should return immediately (fire-and-forget background task)
        var result = await transport.DeferRawAsync(destination, payload, metadata, TimeSpan.FromSeconds(60));

        // Assert — deferred successfully scheduled
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeferRawAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        await transport.DisposeAsync();

        var payload = new byte[] { 1 };
        var metadata = TestMessageContextFactory.CreateMetadata();

        // Act
        Func<Task> act = async () => await transport.DeferRawAsync("q", payload, metadata, TimeSpan.FromSeconds(1));

        // Assert
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task DeferRawAsync_WithNullMetadata_ThrowsArgumentNullException()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        var payload = new byte[] { 1 };

        // Act
        Func<Task> act = async () => await transport.DeferRawAsync("q", payload, null!, TimeSpan.FromSeconds(1));

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DeferRawAsync_WithEmptyDestination_ThrowsArgumentException()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        var payload = new byte[] { 1 };
        var metadata = TestMessageContextFactory.CreateMetadata();

        // Act
        Func<Task> act = async () => await transport.DeferRawAsync(string.Empty, payload, metadata, TimeSpan.FromSeconds(1));

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region PublishBatchRawAsync Tests

    [Fact]
    public async Task PublishBatchRawAsync_EmptyBatch_ReturnsSuccessImmediately()
    {
        var transport = new InMemoryMessageTransport();
        var result = await transport.PublishBatchRawAsync("topic", Array.Empty<(ReadOnlyMemory<byte>, TransportMessageMetadata)>());

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task PublishBatchRawAsync_WithSubscribers_DeliversAllItems()
    {
        var transport = new InMemoryMessageTransport();
        var destination = "batch.orders";
        var received = new System.Collections.Concurrent.ConcurrentBag<ReadOnlyMemory<byte>>();

        await transport.SubscribeAsync(
            destination,
            (p, m, ct) =>
            {
                received.Add(p);
                return ValueTask.FromResult(TransportAckResult.Ack);
            },
            new TransportSubscriptionOptions { MaxConcurrency = 2 });

        var batch = new List<(ReadOnlyMemory<byte>, TransportMessageMetadata)>
        {
            (new byte[] { 1, 2 }, TestMessageContextFactory.CreateMetadata()),
            (new byte[] { 3, 4 }, TestMessageContextFactory.CreateMetadata())
        };

        var result = await transport.PublishBatchRawAsync(destination, batch);

        await Task.Delay(100);

        result.IsSuccess.Should().BeTrue();
        received.Count.Should().Be(2);
    }

    [Fact]
    public async Task PublishBatchRawAsync_WithoutSubscribers_ReturnsSuccessAsUnrouted()
    {
        var transport = new InMemoryMessageTransport();
        var batch = new List<(ReadOnlyMemory<byte>, TransportMessageMetadata)>
        {
            (new byte[] { 1 }, TestMessageContextFactory.CreateMetadata())
        };

        var result = await transport.PublishBatchRawAsync("unrouted.topic", batch);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task PublishBatchRawAsync_NullBatch_ThrowsArgumentNullException()
    {
        var transport = new InMemoryMessageTransport();

        Func<Task> act = async () => await transport.PublishBatchRawAsync("topic", null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PublishBatchRawAsync_InvalidDestination_ThrowsArgumentException(string? invalidDest)
    {
        var transport = new InMemoryMessageTransport();
        var batch = new List<(ReadOnlyMemory<byte>, TransportMessageMetadata)>
        {
            (new byte[] { 1 }, TestMessageContextFactory.CreateMetadata())
        };

        Func<Task> act = async () => await transport.PublishBatchRawAsync(invalidDest!, batch);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PublishBatchRawAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        var transport = new InMemoryMessageTransport();
        await transport.DisposeAsync();

        var batch = new List<(ReadOnlyMemory<byte>, TransportMessageMetadata)>
        {
            (new byte[] { 1 }, TestMessageContextFactory.CreateMetadata())
        };

        Func<Task> act = async () => await transport.PublishBatchRawAsync("topic", batch);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task PublishBatchRawAsync_EmptyBatch_WhenSubscribersExist_DoesNotDeliverAnyMessages()
    {
        var transport = new InMemoryMessageTransport();
        int callCount = 0;
        await transport.SubscribeAsync(
            "topic.empty",
            (p, m, ct) =>
            {
                Interlocked.Increment(ref callCount);
                return ValueTask.FromResult(TransportAckResult.Ack);
            },
            new TransportSubscriptionOptions { MaxConcurrency = 1 });

        var result = await transport.PublishBatchRawAsync("topic.empty", Array.Empty<(ReadOnlyMemory<byte>, TransportMessageMetadata)>());

        result.IsSuccess.Should().BeTrue();
        await Task.Delay(50);
        callCount.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5000)]
    public async Task DeferRawAsync_NegativeAndZeroDelays_PublishesImmediately(int delayMs)
    {
        var transport = new InMemoryMessageTransport();
        var destination = "instant.topic";
        var received = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync(
            destination,
            (p, m, ct) =>
            {
                received.TrySetResult(true);
                return ValueTask.FromResult(TransportAckResult.Ack);
            },
            new TransportSubscriptionOptions { MaxConcurrency = 1 });

        var result = await transport.DeferRawAsync(
            destination,
            new byte[] { 1, 2 },
            TestMessageContextFactory.CreateMetadata(),
            TimeSpan.FromMilliseconds(delayMs));

        result.IsSuccess.Should().BeTrue();
        var completed = await Task.WhenAny(received.Task, Task.Delay(500));
        completed.Should().Be(received.Task);
    }

    [Fact]
    public async Task DeferRawAsync_WithCustomTimeProvider_DeliversMessage()
    {
        var timeProvider = new ManualTimeProvider();
        var transport = new InMemoryMessageTransport(timeProvider: timeProvider);
        var destination = "deferred.topic";
        var receivedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync(
            destination,
            (p, m, ct) =>
            {
                receivedTcs.TrySetResult(true);
                return ValueTask.FromResult(TransportAckResult.Ack);
            },
            new TransportSubscriptionOptions { MaxConcurrency = 1 });

        var result = await transport.DeferRawAsync(
            destination,
            new byte[] { 1 },
            TestMessageContextFactory.CreateMetadata(),
            TimeSpan.FromMilliseconds(50));

        result.IsSuccess.Should().BeTrue();
        var completed = await Task.WhenAny(receivedTcs.Task, Task.Delay(1000));
        completed.Should().Be(receivedTcs.Task);
    }

    [Fact]
    public async Task DeferRawAsync_WhenExceptionOccursInBackground_LogsError()
    {
        var logger = new TestLogger<InMemoryMessageTransport>();
        var throwingTimeProvider = new ThrowingTimeProvider();
        var transport = new InMemoryMessageTransport(logger: logger, timeProvider: throwingTimeProvider);
        var metadata = TestMessageContextFactory.CreateMetadata();

        var result = await transport.DeferRawAsync("failing.dest", new byte[] { 1 }, metadata, TimeSpan.FromSeconds(10));

        result.IsSuccess.Should().BeTrue();
        await Task.Delay(100);

        logger.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error &&
            e.Message.Contains("Error during deferred delivery of message") &&
            e.Message.Contains(metadata.MessageId) &&
            e.Message.Contains("failing.dest"));
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenHandlerThrowsException_LogsErrorAndReleasesSemaphore()
    {
        var logger = new TestLogger<InMemoryMessageTransport>();
        var transport = new InMemoryMessageTransport(logger: logger);
        var metadata = TestMessageContextFactory.CreateMetadata();
        var errorLoggedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync(
            "throw.topic",
            (p, m, ct) =>
            {
                errorLoggedTcs.TrySetResult(true);
                throw new InvalidOperationException("Fatal handler crash");
            },
            new TransportSubscriptionOptions { MaxConcurrency = 1 });

        await transport.PublishRawAsync("throw.topic", new byte[] { 1 }, metadata);

        await errorLoggedTcs.Task;
        await Task.Delay(100);

        logger.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error &&
            e.Message.Contains("Unexpected error processing in-memory message") &&
            e.Message.Contains(metadata.MessageId) &&
            e.Exception != null && e.Exception.Message.Contains("Fatal handler crash"));
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenHandlerReturnsNackRequeue_RequeuesMessage()
    {
        var transport = new InMemoryMessageTransport();
        int attempts = 0;
        var completedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync(
            "requeue.topic",
            (p, m, ct) =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                if (attempt == 1)
                {
                    return ValueTask.FromResult(TransportAckResult.NackRequeue);
                }

                completedTcs.TrySetResult(true);
                return ValueTask.FromResult(TransportAckResult.Ack);
            },
            new TransportSubscriptionOptions { MaxConcurrency = 1 });

        await transport.PublishRawAsync("requeue.topic", new byte[] { 1 }, TestMessageContextFactory.CreateMetadata());

        var completed = await Task.WhenAny(completedTcs.Task, Task.Delay(1000));
        completed.Should().Be(completedTcs.Task);
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task PublishRawAsync_WhenChannelFull_AwaitsWriteAsyncAndDelivers()
    {
        var options = Options.Create(new InMemoryTransportOptions
        {
            ChannelCapacity = 1,
            FullMode = BoundedChannelFullMode.Wait
        });
        var transport = new InMemoryMessageTransport(options: options);

        var firstMessageStartedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unblockFirstTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondMessageReceivedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var meta1 = TestMessageContextFactory.CreateMetadata();
        var meta2 = TestMessageContextFactory.CreateMetadata();
        var meta3 = TestMessageContextFactory.CreateMetadata();

        await transport.SubscribeAsync(
            "backpressure.topic",
            async (p, m, ct) =>
            {
                if (m.MessageId == meta1.MessageId)
                {
                    firstMessageStartedTcs.TrySetResult(true);
                    await unblockFirstTcs.Task;
                    return TransportAckResult.Ack;
                }
                else
                {
                    secondMessageReceivedTcs.TrySetResult(true);
                    return TransportAckResult.Ack;
                }
            },
            new TransportSubscriptionOptions { MaxConcurrency = 1 });

        // Message 1 is consumed immediately and waits on unblockFirstTcs
        await transport.PublishRawAsync("backpressure.topic", new byte[] { 1 }, meta1);
        await firstMessageStartedTcs.Task;

        // Message 2 fills channel capacity (1)
        await transport.PublishRawAsync("backpressure.topic", new byte[] { 2 }, meta2);

        // Message 3 triggers WriteAsync because TryWrite returns false (channel full)
        var publish3Task = Task.Run(async () => await transport.PublishRawAsync("backpressure.topic", new byte[] { 3 }, meta3));

        try
        {
            await Task.Delay(50);
            publish3Task.IsCompleted.Should().BeFalse();
        }
        finally
        {
            // Unblock handler so channel drains
            unblockFirstTcs.TrySetResult(true);
        }

        await publish3Task;
        await secondMessageReceivedTcs.Task;
    }

    [Fact]
    public async Task PublishBatchRawAsync_WhenChannelFull_AwaitsWriteAsyncAndDelivers()
    {
        var options = Options.Create(new InMemoryTransportOptions
        {
            ChannelCapacity = 1,
            FullMode = BoundedChannelFullMode.Wait
        });
        var transport = new InMemoryMessageTransport(options: options);

        var firstMessageStartedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unblockFirstTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var meta1 = TestMessageContextFactory.CreateMetadata();
        var meta2 = TestMessageContextFactory.CreateMetadata();

        await transport.SubscribeAsync(
            "batch.backpressure",
            async (p, m, ct) =>
            {
                if (m.MessageId == meta1.MessageId)
                {
                    firstMessageStartedTcs.TrySetResult(true);
                    await unblockFirstTcs.Task;
                    return TransportAckResult.Ack;
                }
                return TransportAckResult.Ack;
            },
            new TransportSubscriptionOptions { MaxConcurrency = 1 });

        // Fill channel
        await transport.PublishRawAsync("batch.backpressure", new byte[] { 1 }, meta1);
        await firstMessageStartedTcs.Task;

        await transport.PublishRawAsync("batch.backpressure", new byte[] { 2 }, meta2);

        // Publish batch when channel is full (triggers WriteAsync inside batch loop)
        var batch = new List<(ReadOnlyMemory<byte>, TransportMessageMetadata)>
        {
            (new byte[] { 3 }, TestMessageContextFactory.CreateMetadata()),
            (new byte[] { 4 }, TestMessageContextFactory.CreateMetadata())
        };

        var batchPublishTask = Task.Run(async () => await transport.PublishBatchRawAsync("batch.backpressure", batch));

        try
        {
            await Task.Delay(50);
            batchPublishTask.IsCompleted.Should().BeFalse();
        }
        finally
        {
            // Unblock
            unblockFirstTcs.TrySetResult(true);
        }

        await batchPublishTask;
        (await batchPublishTask).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task PublishBatchRawAsync_EmptyBatch_WhenNoSubscribers_ReturnsImmediatelyWithoutLoggingUnrouted()
    {
        var logger = new TestLogger<InMemoryMessageTransport>();
        var transport = new InMemoryMessageTransport(logger: logger);

        var result = await transport.PublishBatchRawAsync("unrouted.topic", Array.Empty<(ReadOnlyMemory<byte>, TransportMessageMetadata)>());

        result.IsSuccess.Should().BeTrue();
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task DeferRawAsync_ZeroDelay_WithThrowingTimeProvider_DoesNotCreateTimerOrLogError()
    {
        var logger = new TestLogger<InMemoryMessageTransport>();
        var throwingTimeProvider = new ThrowingTimeProvider();
        var transport = new InMemoryMessageTransport(logger: logger, timeProvider: throwingTimeProvider);
        var result = await transport.DeferRawAsync("instant.dest", new byte[] { 1 }, TestMessageContextFactory.CreateMetadata(), TimeSpan.Zero);

        result.IsSuccess.Should().BeTrue();
        await Task.Delay(50);
        logger.Entries.Should().NotContain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error);
        logger.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Debug && e.Message.Contains("unrouted"));
    }

    [Fact]
    public async Task SubscribeAsync_WithOptionsFullMode_ConfiguresChannelFullMode()
    {
        var options = Options.Create(new InMemoryTransportOptions
        {
            ChannelCapacity = 1,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        var transport = new InMemoryMessageTransport(options: options);

        var firstReceivedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unblockTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdReceivedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var m1 = TestMessageContextFactory.CreateMetadata();
        var m2 = TestMessageContextFactory.CreateMetadata();
        var m3 = TestMessageContextFactory.CreateMetadata();

        var receivedList = new List<string>();
        await transport.SubscribeAsync(
            "drop.topic",
            async (p, m, ct) =>
            {
                lock (receivedList) { receivedList.Add(m.MessageId); }
                if (m.MessageId == m1.MessageId)
                {
                    firstReceivedTcs.TrySetResult(true);
                    await unblockTcs.Task;
                }
                else
                {
                    thirdReceivedTcs.TrySetResult(true);
                }
                return TransportAckResult.Ack;
            },
            new TransportSubscriptionOptions { MaxConcurrency = 1 });

        try
        {
            await transport.PublishRawAsync("drop.topic", new byte[] { 1 }, m1);
            await firstReceivedTcs.Task;

            // Channel capacity is 1. We publish m2 (stored in channel).
            await transport.PublishRawAsync("drop.topic", new byte[] { 2 }, m2);
            // We publish m3. Since DropOldest is configured, m2 is dropped and m3 is kept without blocking!
            var pub3 = transport.PublishRawAsync("drop.topic", new byte[] { 3 }, m3);
            pub3.IsCompleted.Should().BeTrue();
        }
        finally
        {
            unblockTcs.TrySetResult(true);
        }

        await thirdReceivedTcs.Task;
        lock (receivedList)
        {
            receivedList.Should().Contain(m3.MessageId);
        }
    }

    [Fact]
    public async Task DisposeAsync_WhenDisposed_CleansUpAndRejectsSubsequentOperations()
    {
        // Arrange
        var transport = new InMemoryMessageTransport();
        await transport.SubscribeAsync(
            "test.dispose",
            (p, m, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            new TransportSubscriptionOptions { MaxConcurrency = 1 });

        // Act
        await transport.DisposeAsync();

        // Assert - Subsequent operations throw ObjectDisposedException
        Func<Task> act1 = async () => await transport.PublishRawAsync("test.dispose", new byte[] { 1 }, TestMessageContextFactory.CreateMetadata());
        await act1.Should().ThrowAsync<ObjectDisposedException>();

        Func<Task> act2 = async () => await transport.SubscribeAsync("test.dispose", (p, m, ct) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());
        await act2.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Operations_ConfigureAwaitFalse_DoesNotPostToSynchronizationContext()
    {
        var syncContext = new TrackingSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);

        try
        {
            var options = Options.Create(new InMemoryTransportOptions
            {
                ChannelCapacity = 1,
                FullMode = BoundedChannelFullMode.Wait
            });
            var transport = new InMemoryMessageTransport(options: options);

            // Subscribe and block handler
            var handlerTcs = new TaskCompletionSource<TransportAckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = transport.SubscribeAsync(
                "sync.test",
                (p, m, ct) => new ValueTask<TransportAckResult>(handlerTcs.Task),
                new TransportSubscriptionOptions { MaxConcurrency = 1 });

            // Fill channel
            var meta1 = TestMessageContextFactory.CreateMetadata();
            var meta2 = TestMessageContextFactory.CreateMetadata();
            var meta3 = TestMessageContextFactory.CreateMetadata();

            _ = transport.PublishRawAsync("sync.test", new byte[] { 1 }, meta1);
            _ = transport.PublishRawAsync("sync.test", new byte[] { 2 }, meta2);

            // WriteAsync on full channel in syncContext
            var task3 = transport.PublishRawAsync("sync.test", new byte[] { 3 }, meta3).AsTask();
            var batchTask = transport.PublishBatchRawAsync("sync.test", new[] { (new ReadOnlyMemory<byte>(new byte[] { 4 }), TestMessageContextFactory.CreateMetadata()) }).AsTask();

            // Defer with Zero delay
            var deferZeroTask = transport.DeferRawAsync("sync.test", new byte[] { 5 }, TestMessageContextFactory.CreateMetadata(), TimeSpan.Zero).AsTask();

            // Release handler
            handlerTcs.TrySetResult(TransportAckResult.Ack);

            ((IAsyncResult)Task.WhenAll(task3, batchTask, deferZeroTask)).AsyncWaitHandle.WaitOne(5000);

            syncContext.PostCount.Should().Be(0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    [Fact]
    public async Task DeferRawAsync_ZeroDelay_WhenChannelFullAndTokenCancelled_ThrowsOperationCanceledExceptionImmediately()
    {
        var options = Options.Create(new InMemoryTransportOptions
        {
            ChannelCapacity = 1,
            FullMode = BoundedChannelFullMode.Wait
        });
        var transport = new InMemoryMessageTransport(options: options);

        var firstStartedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unblockTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var meta1 = TestMessageContextFactory.CreateMetadata();
        await transport.SubscribeAsync(
            "cancel.topic",
            async (p, m, ct) =>
            {
                firstStartedTcs.TrySetResult(true);
                await unblockTcs.Task;
                return TransportAckResult.Ack;
            },
            new TransportSubscriptionOptions { MaxConcurrency = 1 });

        // Message 1 consumed and waits on unblockTcs
        await transport.PublishRawAsync("cancel.topic", new byte[] { 1 }, meta1);
        await firstStartedTcs.Task;

        // Message 2 fills channel
        await transport.PublishRawAsync("cancel.topic", new byte[] { 2 }, TestMessageContextFactory.CreateMetadata());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            // Since delay is Zero, it synchronously calls PublishRawAsync -> WriteAsync with cancelled token -> throws OperationCanceledException
            Func<Task> act = async () => await transport.DeferRawAsync("cancel.topic", new byte[] { 3 }, TestMessageContextFactory.CreateMetadata(), TimeSpan.Zero, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            unblockTcs.TrySetResult(true);
        }
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection test")]
    public void ExecuteDeferredDeliveryAsync_ConfigureAwaitFalse_DoesNotPostToSynchronizationContext()
    {
        var syncContext = new TrackingSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);

        try
        {
            var options = Options.Create(new InMemoryTransportOptions
            {
                ChannelCapacity = 1,
                FullMode = BoundedChannelFullMode.Wait
            });
            var transport = new InMemoryMessageTransport(options: options);

            var firstStartedMre = new ManualResetEventSlim(false);
            var unblockMre = new ManualResetEventSlim(false);

            var meta1 = TestMessageContextFactory.CreateMetadata();
            _ = transport.SubscribeAsync(
                "sync.deferred",
                (p, m, ct) =>
                {
                    firstStartedMre.Set();
                    unblockMre.Wait(5000, ct);
                    return ValueTask.FromResult(TransportAckResult.Ack);
                },
                new TransportSubscriptionOptions { MaxConcurrency = 1 });

            // Fill channel so PublishRawAsync will yield on WriteAsync
            _ = transport.PublishRawAsync("sync.deferred", new byte[] { 1 }, meta1);
            firstStartedMre.Wait(5000);
            _ = transport.PublishRawAsync("sync.deferred", new byte[] { 2 }, TestMessageContextFactory.CreateMetadata());

            var method = typeof(InMemoryMessageTransport).GetMethod("ExecuteDeferredDeliveryAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            // With delay = TimeSpan.Zero, Task.Delay completes synchronously, so PublishRawAsync starts on thread with syncContext active and yields asynchronously
            var task = (Task)method.Invoke(transport, new object[] { "sync.deferred", new ReadOnlyMemory<byte>(new byte[] { 3 }), TestMessageContextFactory.CreateMetadata(), TimeSpan.Zero, CancellationToken.None })!;

            // Unblock subscriber so WriteAsync completes
            unblockMre.Set();

            ((IAsyncResult)task).AsyncWaitHandle.WaitOne(5000);

            syncContext.PostCount.Should().Be(0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection test")]
    public void ExecuteDeferredDeliveryAsync_WhenDelayYields_ConfigureAwaitFalse_DoesNotPostToSynchronizationContext()
    {
        var syncContext = new TrackingSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);

        try
        {
            var transport = new InMemoryMessageTransport();
            var method = typeof(InMemoryMessageTransport).GetMethod("ExecuteDeferredDeliveryAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var task = (Task)method.Invoke(transport, new object[] { "dest", new ReadOnlyMemory<byte>(new byte[] { 1 }), TestMessageContextFactory.CreateMetadata(), TimeSpan.FromMilliseconds(10), CancellationToken.None })!;
            ((IAsyncResult)task).AsyncWaitHandle.WaitOne(5000);

            syncContext.PostCount.Should().Be(0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection test")]
    public void ProcessMessageAsync_WhenRequeueYields_ConfigureAwaitFalse_DoesNotPostToSynchronizationContext()
    {
        var syncContext = new TrackingSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);

        try
        {
            var options = Options.Create(new InMemoryTransportOptions
            {
                ChannelCapacity = 1,
                FullMode = BoundedChannelFullMode.Wait
            });
            var transport = new InMemoryMessageTransport(options: options);
            _ = transport.SubscribeAsync("test.requeue.sync", (p, m, ct) => ValueTask.FromResult(TransportAckResult.NackRequeue), new TransportSubscriptionOptions { MaxConcurrency = 1 });
            var subsField = typeof(InMemoryMessageTransport).GetField("_subscriptions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var subsDict = (System.Collections.IDictionary)subsField.GetValue(transport)!;
            var list = (System.Collections.IList)subsDict["test.requeue.sync"]!;
            var entry = list[0]!;

            var packetType = typeof(InMemoryMessageTransport).GetNestedType("InMemoryPacket", System.Reflection.BindingFlags.NonPublic)!;
            var packet1 = Activator.CreateInstance(packetType, new ReadOnlyMemory<byte>(new byte[] { 1 }), TestMessageContextFactory.CreateMetadata())!;
            var packet2 = Activator.CreateInstance(packetType, new ReadOnlyMemory<byte>(new byte[] { 2 }), TestMessageContextFactory.CreateMetadata())!;

            var channelProp = entry.GetType().GetProperty("Channels")!;
            var channelsArray = (Array)channelProp.GetValue(entry)!;
            var channel = channelsArray.GetValue(0)!;
            var writerProp = channel.GetType().GetProperty("Writer")!;
            var writer = writerProp.GetValue(channel)!;
            var tryWriteMethod = writer.GetType().GetMethod("TryWrite")!;
            // Fill channel capacity (1)
            tryWriteMethod.Invoke(writer, new object[] { packet1 });

            var semaphore = new SemaphoreSlim(0, 1);
            // Synchronous NackRequeue handler
            Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>> handler = (p, m, ct) => ValueTask.FromResult(TransportAckResult.NackRequeue);

            var method = typeof(InMemoryMessageTransport).GetMethod("ProcessMessageAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            // Line 250 completes synchronously, then line 253 calls WriteAsync which yields because channel is full
            var task = (Task)method.Invoke(transport, new object[] { channel, handler, packet2, CancellationToken.None })!;

            // Now drain channel so WriteAsync completes
            var readerProp = channel.GetType().GetProperty("Reader")!;
            var reader = readerProp.GetValue(channel)!;
            var tryReadMethod = reader.GetType().GetMethod("TryRead")!;
            var args = new object?[] { null };
            tryReadMethod.Invoke(reader, args);

            ((IAsyncResult)task).AsyncWaitHandle.WaitOne(5000);

            syncContext.PostCount.Should().Be(0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    [Fact]
    public async Task Dispose_Synchronous_CompletesChannelsAndCancelsLoops()
    {
        var transport = new InMemoryMessageTransport();
        await transport.SubscribeAsync(
            "disp.topic",
            (p, m, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            new TransportSubscriptionOptions { MaxConcurrency = 1 });

        transport.Dispose();

        Func<Task> publishAct = async () => await transport.PublishRawAsync("disp.topic", new byte[] { 1 }, TestMessageContextFactory.CreateMetadata());
        await publishAct.Should().ThrowAsync<ObjectDisposedException>();

        // Second dispose does not throw
        Action secondDispose = () => transport.Dispose();
        secondDispose.Should().NotThrow();
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public readonly List<(LogLevel Level, string Message, Exception? Exception)> Entries = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class ThrowingTimeProvider : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            throw new InvalidOperationException("Time provider exploded");
        }
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
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection test")]
    public async Task RunSubscriptionLoopAsync_WhenReadCancelled_ExitsLoopGracefully()
    {
        var transport = new InMemoryMessageTransport();
        var options = Options.Create(new InMemoryTransportOptions());
        var packetType = typeof(InMemoryMessageTransport).GetNestedType("InMemoryPacket", System.Reflection.BindingFlags.NonPublic)!;
        
        var createBoundedMethod = typeof(Channel).GetMethod("CreateBounded", new[] { typeof(int) })!.MakeGenericMethod(packetType);
        var actualChannel = createBoundedMethod.Invoke(null, new object[] { 1 })!;

        Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>> handler = (p, m, ct) => ValueTask.FromResult(TransportAckResult.Ack);

        var method = typeof(InMemoryMessageTransport).GetMethod("RunSubscriptionLoopAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var cts = new CancellationTokenSource();
        
        var task = (Task)method.Invoke(transport, new object[] { actualChannel, handler, cts.Token })!;

        cts.Cancel();

        await task.WaitAsync(TimeSpan.FromSeconds(5));
        task.IsCompletedSuccessfully.Should().BeTrue();
        await transport.DisposeAsync();
    }

    [Fact]
    public void GetPartitionIndex_InitialRoundRobinCounter_StartsAtZeroAndCycles()
    {
        using var transport = new InMemoryMessageTransport();
        var method = typeof(InMemoryMessageTransport).GetMethod("GetPartitionIndex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        // Partition count 3: with counter starting at -1, first call yields (-1+1)%3 = 0
        // If counter mutated to +1, first call would yield (1+1)%3 = 2!
        var idx0 = (int)method.Invoke(transport, new object?[] { null, 3 })!;
        var idx1 = (int)method.Invoke(transport, new object?[] { null, 3 })!;
        var idx2 = (int)method.Invoke(transport, new object?[] { null, 3 })!;
        var idx3 = (int)method.Invoke(transport, new object?[] { null, 3 })!;

        idx0.Should().Be(0);
        idx1.Should().Be(1);
        idx2.Should().Be(2);
        idx3.Should().Be(0);
    }

    [Fact]
    public void GetPartitionIndex_PartitionCountOne_ReturnsZeroImmediately()
    {
        using var transport = new InMemoryMessageTransport();
        var method = typeof(InMemoryMessageTransport).GetMethod("GetPartitionIndex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var resNull = (int)method.Invoke(transport, new object?[] { null, 1 })!;
        var resKey = (int)method.Invoke(transport, new object?[] { "any-key", 1 })!;

        resNull.Should().Be(0);
        resKey.Should().Be(0);
    }

    [Fact]
    public void GetPartitionIndex_EmptyStringPartitionKey_UsesRoundRobin()
    {
        using var transport = new InMemoryMessageTransport();
        var method = typeof(InMemoryMessageTransport).GetMethod("GetPartitionIndex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var idx0 = (int)method.Invoke(transport, new object?[] { "", 3 })!;
        var idx1 = (int)method.Invoke(transport, new object?[] { "", 3 })!;

        idx0.Should().Be(0);
        idx1.Should().Be(1);
    }

    [Fact]
    public void GetPartitionIndex_ExplicitKey_CalculatesModuloHash()
    {
        using var transport = new InMemoryMessageTransport();
        var method = typeof(InMemoryMessageTransport).GetMethod("GetPartitionIndex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var key = "user-12345";
        var expected = Math.Abs(key.GetHashCode(StringComparison.Ordinal)) % 7;
        var actual = (int)method.Invoke(transport, new object?[] { key, 7 })!;

        actual.Should().Be(expected);
    }

    [Fact]
    public async Task DisposeAsync_ClearsSubscriptions()
    {
        var transport = new InMemoryMessageTransport();
        await transport.SubscribeAsync("clear.topic", (p, m, ct) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions { MaxConcurrency = 1 });

        var field = typeof(InMemoryMessageTransport).GetField("_subscriptions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var subscriptions = (System.Collections.IDictionary)field.GetValue(transport)!;
        subscriptions.Count.Should().Be(1);

        await transport.DisposeAsync();

        subscriptions.Count.Should().Be(0);
    }

    #endregion
}











