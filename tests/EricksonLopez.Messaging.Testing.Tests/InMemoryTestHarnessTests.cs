// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Testing;
using EricksonLopez.Messaging.Transport;
using Xunit;

namespace EricksonLopez.Messaging.Testing.Tests;

[Trait("Category", "Unit")]
public class InMemoryTestHarnessTests
{
    private static TransportMessageMetadata CreateMetadata(string messageType = "test.message", string? messageId = null) =>
        new(
            MessageId: messageId ?? Guid.NewGuid().ToString("N"),
            MessageType: messageType,
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: Guid.NewGuid().ToString("N"),
            CausationId: null,
            TraceParent: null,
            TenantId: "tenant-1",
            PartitionKey: null,
            ContentType: "application/json",
            SchemaVersion: 1,
            Headers: new Dictionary<string, string>());

    [Fact]
    public async Task PublishRawAsync_ValidMessage_RecordsInPublishedMessagesAndSucceeds()
    {
        // Arrange
        await using var harness = new InMemoryTestHarness();
        var payload = Encoding.UTF8.GetBytes("{\"value\":42}");
        var metadata = CreateMetadata("OrderCreated");

        // Act
        var result = await harness.PublishRawAsync("orders", payload, metadata);

        // Assert
        result.IsSuccess.Should().BeTrue();
        harness.PublishedMessages.Should().HaveCount(1);
        harness.PublishedMessages.Contains("OrderCreated").Should().BeTrue();
        var published = harness.PublishedMessages.OfType("OrderCreated").First();
        published.Destination.Should().Be("orders");
        published.Metadata.MessageId.Should().Be(metadata.MessageId);
        published.Payload.ToArray().Should().BeEquivalentTo(payload);
    }

    [Fact]
    public async Task SubscribeAsync_NullHandler_ThrowsArgumentNullException()
    {
        // Arrange
        await using var harness = new InMemoryTestHarness();

        // Act
        var act = async () => await harness.SubscribeAsync("orders", null!, new TransportSubscriptionOptions());

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("messageHandler");
    }

    [Fact]
    public async Task SubscribeAsync_NullDestination_ThrowsArgumentNullException()
    {
        // Arrange
        await using var harness = new InMemoryTestHarness();

        // Act
        var act = async () => await harness.SubscribeAsync(null!, (_, _, _) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("destination");
    }

    [Fact]
    public async Task SubscribeAsync_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        await using var harness = new InMemoryTestHarness();

        // Act
        var act = async () => await harness.SubscribeAsync("orders", (_, _, _) => ValueTask.FromResult(TransportAckResult.Ack), null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task SubscribeAsync_PendingMessagesForDestination_InvokesHandlerAndRecordsSuccessOnAck()
    {
        // Arrange
        await using var harness = new InMemoryTestHarness();
        var payload = Encoding.UTF8.GetBytes("{\"value\":100}");
        var metadata = CreateMetadata("InvoiceIssued");

        await harness.PublishRawAsync("invoices", payload, metadata);
        await harness.PublishRawAsync("other-topic", payload, CreateMetadata("OtherMsg"));

        // Act
        var invoked = 0;
        var subResult = await harness.SubscribeAsync("invoices", (p, m, ct) =>
        {
            invoked++;
            return ValueTask.FromResult(TransportAckResult.Ack);
        }, new TransportSubscriptionOptions());

        // Assert
        subResult.IsSuccess.Should().BeTrue();
        invoked.Should().Be(1);
        harness.ConsumedMessages.Should().HaveCount(1);
        harness.ConsumedMessages.Contains("InvoiceIssued").Should().BeTrue();
        harness.ConsumedMessages.AnySucceeded("InvoiceIssued").Should().BeTrue();

        var consumed = harness.ConsumedMessages.OfType("InvoiceIssued").First();
        consumed.Destination.Should().Be("invoices");
        consumed.Succeeded.Should().BeTrue();
        consumed.Payload.ToArray().Should().BeEquivalentTo(payload);
    }

    [Fact]
    public async Task SubscribeAsync_PendingMessagesHandlerReturnsNack_RecordsSucceededAsFalse()
    {
        // Arrange
        await using var harness = new InMemoryTestHarness();
        var payload = Encoding.UTF8.GetBytes("{\"value\":200}");
        var metadata = CreateMetadata("PaymentFailed");

        await harness.PublishRawAsync("payments", payload, metadata);

        // Act
        var subResult = await harness.SubscribeAsync("payments", (_, _, _) =>
            ValueTask.FromResult(TransportAckResult.NackRequeue),
            new TransportSubscriptionOptions());

        // Assert
        subResult.IsSuccess.Should().BeTrue();
        harness.ConsumedMessages.Should().HaveCount(1);
        harness.ConsumedMessages.Contains("PaymentFailed").Should().BeTrue();
        harness.ConsumedMessages.AnySucceeded("PaymentFailed").Should().BeFalse();

        var consumed = harness.ConsumedMessages.OfType("PaymentFailed").First();
        consumed.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task WaitUntilPublishedAsync_MessageAlreadyPublished_ReturnsTrueImmediately()
    {
        // Arrange
        await using var harness = new InMemoryTestHarness();
        await harness.PublishRawAsync("queue", Encoding.UTF8.GetBytes("{}"), CreateMetadata("ExistingType"));

        // Act
        var found = await harness.WaitUntilPublishedAsync("ExistingType", TimeSpan.FromSeconds(1));

        // Assert
        found.Should().BeTrue();
    }

    [Fact]
    public async Task WaitUntilPublishedAsync_MessagePublishedConcurrently_ReturnsTrue()
    {
        // Arrange
        await using var harness = new InMemoryTestHarness();

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            await harness.PublishRawAsync("queue", Encoding.UTF8.GetBytes("{}"), CreateMetadata("AsyncType"));
        });

        // Act
        var found = await harness.WaitUntilPublishedAsync("AsyncType", TimeSpan.FromSeconds(2));

        // Assert
        found.Should().BeTrue();
    }

    [Fact]
    public async Task WaitUntilPublishedAsync_MessageNeverPublished_ReturnsFalseOnTimeout()
    {
        // Arrange
        await using var harness = new InMemoryTestHarness();

        // Act
        var found = await harness.WaitUntilPublishedAsync("NonExistentType", TimeSpan.FromMilliseconds(50));

        // Assert
        found.Should().BeFalse();
    }

    [Fact]
    public async Task WaitUntilPublishedAsync_WhenCancelled_ReturnsFalse()
    {
        // Arrange
        await using var harness = new InMemoryTestHarness();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var found = await harness.WaitUntilPublishedAsync("NonExistent", TimeSpan.FromSeconds(5), cts.Token);

        // Assert
        found.Should().BeFalse();
    }

    [Fact]
    public async Task WaitUntilConsumedAsync_MessageAlreadyConsumed_ReturnsTrueImmediately()
    {
        // Arrange
        await using var harness = new InMemoryTestHarness();
        var payload = Encoding.UTF8.GetBytes("{}");
        await harness.PublishRawAsync("queue", payload, CreateMetadata("ConsumedExistingType"));
        await harness.SubscribeAsync("queue", (_, _, _) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());

        // Act
        var found = await harness.WaitUntilConsumedAsync("ConsumedExistingType", TimeSpan.FromSeconds(1));

        // Assert
        found.Should().BeTrue();
    }

    [Fact]
    public async Task WaitUntilConsumedAsync_MessageConsumedConcurrently_ReturnsTrue()
    {
        // Arrange
        await using var harness = new InMemoryTestHarness();

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            await harness.PublishRawAsync("queue", Encoding.UTF8.GetBytes("{}"), CreateMetadata("AsyncConsumedType"));
            await harness.SubscribeAsync("queue", (_, _, _) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());
        });

        // Act
        var found = await harness.WaitUntilConsumedAsync("AsyncConsumedType", TimeSpan.FromSeconds(2));

        // Assert
        found.Should().BeTrue();
    }

    [Fact]
    public async Task WaitUntilConsumedAsync_MessageNeverConsumed_ReturnsFalseOnTimeout()
    {
        // Arrange
        await using var harness = new InMemoryTestHarness();

        // Act
        var found = await harness.WaitUntilConsumedAsync("NonExistentConsumedType", TimeSpan.FromMilliseconds(50));

        // Assert
        found.Should().BeFalse();
    }

    [Fact]
    public async Task WaitUntilConsumedAsync_WhenCancelled_ReturnsFalse()
    {
        // Arrange
        await using var harness = new InMemoryTestHarness();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var found = await harness.WaitUntilConsumedAsync("NonExistentConsumed", TimeSpan.FromSeconds(5), cts.Token);

        // Assert
        found.Should().BeFalse();
    }

    [Fact]
    public async Task WaitUntilPublishedAsync_CalledMultipleTimes_CleansUpWaiterInFinally()
    {
        // Arrange
        await using var harness = new InMemoryTestHarness();
        await harness.WaitUntilPublishedAsync("TypeA", TimeSpan.FromMilliseconds(20));

        // Act
        _ = Task.Run(async () =>
        {
            await Task.Delay(20);
            await harness.PublishRawAsync("q", Encoding.UTF8.GetBytes("{}"), CreateMetadata("TypeA"));
        });

        var found = await harness.WaitUntilPublishedAsync("TypeA", TimeSpan.FromSeconds(2));

        // Assert
        found.Should().BeTrue();
    }

    [Fact]
    public async Task WaitUntilConsumedAsync_CalledMultipleTimes_CleansUpWaiterInFinally()
    {
        // Arrange
        await using var harness = new InMemoryTestHarness();
        await harness.WaitUntilConsumedAsync("TypeB", TimeSpan.FromMilliseconds(20));

        // Act
        _ = Task.Run(async () =>
        {
            await Task.Delay(20);
            await harness.PublishRawAsync("q", Encoding.UTF8.GetBytes("{}"), CreateMetadata("TypeB"));
            await harness.SubscribeAsync("q", (_, _, _) => ValueTask.FromResult(TransportAckResult.Ack), new TransportSubscriptionOptions());
        });

        var found = await harness.WaitUntilConsumedAsync("TypeB", TimeSpan.FromSeconds(2));

        // Assert
        found.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_WhenInvoked_CompletesCleanly()
    {
        // Arrange
        var harness = new InMemoryTestHarness();

        // Act & Assert
        Func<Task> act = async () => await harness.DisposeAsync();
        await act.Should().NotThrowAsync();
    }
}

