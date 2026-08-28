// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Tests.PubSub;

using AwesomeAssertions;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Dispatch;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Trait("Category", "Integration")]
public class MessagePublisherConsumerIntegrationTests
{
    public sealed class TestReceiptTracker
    {
        public OrderPlacedIntegrationEvent? LastReceived { get; set; }
        public TaskCompletionSource<bool> Signal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [MessageType("integration.order-placed.v1")]
    public sealed record OrderPlacedIntegrationEvent(Guid OrderId, decimal Amount) : IMessage;

    public sealed class OrderPlacedConsumer : IMessageHandler<OrderPlacedIntegrationEvent>
    {
        private readonly TestReceiptTracker _tracker;

        public OrderPlacedConsumer(TestReceiptTracker tracker)
        {
            _tracker = tracker;
        }

        public ValueTask<Result> HandleAsync(
            OrderPlacedIntegrationEvent message,
            MessageContext context,
            CancellationToken cancellationToken = default)
        {
            _tracker.LastReceived = message;
            _tracker.Signal.TrySetResult(true);
            return ValueTask.FromResult(Result.Success());
        }
    }

    [Fact]
    public async Task EndToEnd_PublishAndConsume_SuccessfullyProcessesMessage()
    {
        // Arrange
        var tracker = new TestReceiptTracker();

        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddLogging();
        services.AddMessaging();
        services.AddMessageHandler<OrderPlacedIntegrationEvent, OrderPlacedConsumer>();

        var sp = services.BuildServiceProvider();
        var publisher = sp.GetRequiredService<IMessagePublisher>();
        var consumer = sp.GetRequiredService<IMessageConsumer>();

        // Register destination and start consumer
        if (consumer is MessageConsumer concreteConsumer)
        {
            concreteConsumer.AddDestination("integration.order-placed.v1");
        }
        await consumer.StartAsync();

        var message = new OrderPlacedIntegrationEvent(Guid.NewGuid(), 99.95m);

        // Act
        var publishResult = await publisher.PublishAsync(message);

        // Assert Publish
        publishResult.IsSuccess.Should().BeTrue();

        // Await consumption
        var completed = await Task.WhenAny(tracker.Signal.Task, Task.Delay(3000));
        completed.Should().BeSameAs(tracker.Signal.Task);
        tracker.LastReceived.Should().NotBeNull();
        tracker.LastReceived!.OrderId.Should().Be(message.OrderId);
        tracker.LastReceived.Amount.Should().Be(message.Amount);

        // Cleanup
        await consumer.StopReceivingAsync();
        await consumer.DrainInFlightMessagesAsync();
    }
}




