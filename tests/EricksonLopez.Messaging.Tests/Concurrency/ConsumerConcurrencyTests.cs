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
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
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

    [Fact]
    public async Task HighConcurrency_1000ConcurrentOperations_AllProcessedWithoutLoss()
    {
        // Arrange: 1,000 concurrent operations
        const int totalMessages = 1000;
        var tracker = new ConcurrentTrackingCounter(totalMessages);

        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddLogging();
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
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

        // Act - 1,000 concurrent tasks
        var publishTasks = new Task[totalMessages];
        for (int i = 0; i < totalMessages; i++)
        {
            var msg = new BatchTaskMessage(i);
            publishTasks[i] = publisher.PublishAsync(msg).AsTask();
        }
        await Task.WhenAll(publishTasks);

        var completed = await Task.WhenAny(tracker.CompletionTask, Task.Delay(10000));
        completed.Should().BeSameAs(tracker.CompletionTask);

        tracker.Count.Should().Be(totalMessages);

        await consumer.StopReceivingAsync();
        await consumer.DrainInFlightMessagesAsync();
    }

    [Fact]
    public async Task Concurrency_10ThreadsStressLoop_NoLostMessages()
    {
        // 10 worker threads in parallel, each doing 50 publications (500 total)
        const int threadCount = 10;
        const int messagesPerThread = 50;
        const int totalExpected = threadCount * messagesPerThread;

        var tracker = new ConcurrentTrackingCounter(totalExpected);

        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddLogging();
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
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

        var workers = new Task[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            int threadId = t;
            workers[t] = Task.Run(async () =>
            {
                for (int m = 0; m < messagesPerThread; m++)
                {
                    await publisher.PublishAsync(new BatchTaskMessage(threadId * 1000 + m));
                }
            });
        }

        await Task.WhenAll(workers);

        var completed = await Task.WhenAny(tracker.CompletionTask, Task.Delay(10000));
        completed.Should().BeSameAs(tracker.CompletionTask);

        tracker.Count.Should().Be(totalExpected);

        await consumer.StopReceivingAsync();
        await consumer.DrainInFlightMessagesAsync();
    }

    [Fact]
    public async Task Concurrency_100ThreadsStressLoop_NoLostMessages()
    {
        // 100 parallel workers, each doing 10 publications (1,000 total)
        const int threadCount = 100;
        const int messagesPerThread = 10;
        const int totalExpected = threadCount * messagesPerThread;

        var tracker = new ConcurrentTrackingCounter(totalExpected);

        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddLogging();
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
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

        var workers = new Task[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            int threadId = t;
            workers[t] = Task.Run(async () =>
            {
                for (int m = 0; m < messagesPerThread; m++)
                {
                    await publisher.PublishAsync(new BatchTaskMessage(threadId * 1000 + m));
                }
            });
        }

        await Task.WhenAll(workers);

        var completed = await Task.WhenAny(tracker.CompletionTask, Task.Delay(10000));
        completed.Should().BeSameAs(tracker.CompletionTask);

        tracker.Count.Should().Be(totalExpected);

        await consumer.StopReceivingAsync();
        await consumer.DrainInFlightMessagesAsync();
    }

    [Fact]
    public async Task Concurrency_10000RapidOperations_AllProcessedWithoutDeadlock()
    {
        // 10,000 fast in-memory operations
        const int totalOperations = 10000;
        var tracker = new ConcurrentTrackingCounter(totalOperations);

        var services = new ServiceCollection();
        services.AddSingleton(tracker);
        services.AddLogging();
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddMessaging();
        // Fast handler (no delay) to handle 10k ops quickly
        services.AddMessageHandler<FastMessage, FastMessageHandler>();

        var sp = services.BuildServiceProvider();
        var publisher = sp.GetRequiredService<IMessagePublisher>();
        var consumer = sp.GetRequiredService<IMessageConsumer>();

        if (consumer is MessageConsumer concreteConsumer)
        {
            concreteConsumer.AddDestination("concurrency.fast-task.v1");
        }
        await consumer.StartAsync();

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 };
        await Parallel.ForAsync(0, totalOperations, parallelOptions, async (i, ct) =>
        {
            await publisher.PublishAsync(new FastMessage(i), cancellationToken: ct);
        });

        var completed = await Task.WhenAny(tracker.CompletionTask, Task.Delay(15000));
        completed.Should().BeSameAs(tracker.CompletionTask);

        tracker.Count.Should().Be(totalOperations);

        await consumer.StopReceivingAsync();
        await consumer.DrainInFlightMessagesAsync();
    }

    [MessageType("concurrency.fast-task.v1")]
    public sealed record FastMessage(int Id) : IMessage;

    public sealed class FastMessageHandler : IMessageHandler<FastMessage>
    {
        private readonly ConcurrentTrackingCounter _tracker;

        public FastMessageHandler(ConcurrentTrackingCounter tracker)
        {
            _tracker = tracker;
        }

        public ValueTask<Result> HandleAsync(
            FastMessage message,
            MessageContext context,
            CancellationToken cancellationToken = default)
        {
            _tracker.Increment();
            return ValueTask.FromResult(Result.Success());
        }
    }
}





