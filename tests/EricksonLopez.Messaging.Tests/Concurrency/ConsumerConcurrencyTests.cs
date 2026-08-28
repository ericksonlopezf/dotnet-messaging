// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Tests.Concurrency;

using AwesomeAssertions;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Dispatch;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Trait("Category", "Concurrency")]
public class ConsumerConcurrencyTests
{
    public sealed class ConcurrentTrackingCounter
    {
        private int _count;
        private readonly int _targetCount;
        private readonly TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentTrackingCounter(int targetCount = 100)
        {
            _targetCount = targetCount;
        }

        public int Count => Volatile.Read(ref _count);
        public Task CompletionTask => _tcs.Task;

        public void Increment()
        {
            if (Interlocked.Increment(ref _count) >= _targetCount)
            {
                _tcs.TrySetResult(true);
            }
        }
    }

    [MessageType("concurrency.batch-task.v1")]
    public sealed record BatchTaskMessage(int Sequence) : IMessage;

    public sealed class BatchTaskHandler : IMessageHandler<BatchTaskMessage>
    {
        private readonly ConcurrentTrackingCounter _tracker;

        public BatchTaskHandler(ConcurrentTrackingCounter tracker)
        {
            _tracker = tracker;
        }

        public async ValueTask<Result> HandleAsync(
            BatchTaskMessage message,
            MessageContext context,
            CancellationToken cancellationToken = default)
        {
            // Simulate 10ms of async work
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            _tracker.Increment();
            return Result.Success();
        }
    }

    [Fact]
    public async Task HighConcurrency_Publish100Messages_AllProcessedWithoutLoss()
    {
        // Arrange
        const int totalMessages = 100;
        var tracker = new ConcurrentTrackingCounter(totalMessages);

        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddLogging();
        services.AddMessaging();
        services.AddMessageHandler<BatchTaskMessage, BatchTaskHandler>();

        var sp = services.BuildServiceProvider();
        var publisher = sp.GetRequiredService<IMessagePublisher>();
        var consumer = sp.GetRequiredService<IMessageConsumer>();

        if (consumer is MessageConsumer concreteConsumer)
        {
            concreteConsumer.AddDestination("concurrency.batch-task.v1");
        }
        await consumer.StartAsync();

        // Act - Publish 100 messages concurrently
        var publishTasks = new Task[totalMessages];
        for (int i = 0; i < totalMessages; i++)
        {
            var msg = new BatchTaskMessage(i);
            publishTasks[i] = publisher.PublishAsync(msg).AsTask();
        }
        await Task.WhenAll(publishTasks);

        // Wait for all messages to be processed reactively
        var completed = await Task.WhenAny(tracker.CompletionTask, Task.Delay(5000));
        completed.Should().BeSameAs(tracker.CompletionTask);

        // Assert
        tracker.Count.Should().Be(totalMessages);

        // Graceful Drain and Shutdown
        await consumer.StopReceivingAsync();
        await consumer.DrainInFlightMessagesAsync();
    }
}




