// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Dispatch;
using EricksonLopez.Messaging.Middleware;
namespace EricksonLopez.Messaging.Tests.Chaos;

using Result = EricksonLopez.Result.Result;
using Error = EricksonLopez.Result.Error;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Trait("Category", "Chaos")]
public class MessagingChaosTests
{
    [MessageType("chaos.flaky-order.v1")]
    public sealed record FlakyOrderMessage(Guid OrderId, int AttemptsBeforeSuccess) : IMessage;

    public sealed class FlakyOrderTracker
    {
        private int _attempts;
        public int TotalAttempts => _attempts;
        public void RecordAttempt() => Interlocked.Increment(ref _attempts);
    }

    public sealed class FlakyOrderHandler : IMessageHandler<FlakyOrderMessage>
    {
        private readonly FlakyOrderTracker _tracker;
        public FlakyOrderHandler(FlakyOrderTracker tracker) => _tracker = tracker;

        public ValueTask<Result> HandleAsync(
            FlakyOrderMessage message,
            MessageContext context,
            CancellationToken cancellationToken = default)
        {
            _tracker.RecordAttempt();
            if (_tracker.TotalAttempts <= message.AttemptsBeforeSuccess)
            {
                return ValueTask.FromResult(Result.Failure(Error.Failure(
                    code: "Chaos.FlakyFailure",
                    description: $"Transient chaos failure on attempt {_tracker.TotalAttempts}")));
            }

            return ValueTask.FromResult(Result.Success());
        }
    }

    [Fact]
    public async Task Chaos_FlakyHandler_RetryMiddlewareRecoversSuccessfully()
    {
        // Arrange: Handler fails twice, succeeds on 3rd attempt.
        var tracker = new FlakyOrderTracker();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(tracker);
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddMessaging(options =>
        {
            options.AddRetry(retry =>
            {
                retry.MaxRetries = 4;
                retry.InitialDelay = TimeSpan.FromMilliseconds(5);
                retry.MaxDelay = TimeSpan.FromMilliseconds(20);
            });
        });
        services.AddMessageHandler<FlakyOrderMessage, FlakyOrderHandler>();

        var sp = services.BuildServiceProvider();
        var publisher = sp.GetRequiredService<IMessagePublisher>();
        var consumer = sp.GetRequiredService<IMessageConsumer>();

        if (consumer is MessageConsumer concreteConsumer)
        {
            concreteConsumer.AddDestination("chaos.flaky-order.v1");
        }
        await consumer.StartAsync();

        // Act
        var publishResult = await publisher.PublishAsync(new FlakyOrderMessage(Guid.NewGuid(), 2));
        publishResult.IsSuccess.Should().BeTrue();

        // Allow retry loop to complete
        await Task.Delay(300);

        // Assert
        tracker.TotalAttempts.Should().Be(3);

        await consumer.StopReceivingAsync();
        await consumer.DrainInFlightMessagesAsync();
    }

    [MessageType("chaos.hung-task.v1")]
    public sealed record HungTaskMessage(Guid TaskId) : IMessage;

    public sealed class HungTaskHandler : IMessageHandler<HungTaskMessage>
    {
        public async ValueTask<Result> HandleAsync(
            HungTaskMessage message,
            MessageContext context,
            CancellationToken cancellationToken = default)
        {
            // Simulates hung thread waiting indefinitely until cancelled
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return Result.Success();
        }
    }

    [Fact]
    public async Task Chaos_HungHandler_TimeoutMiddlewareCancelsAndReturnsTimeoutError()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddMessaging(options =>
        {
            options.AddHandlerTimeout(TimeSpan.FromMilliseconds(50));
        });
        services.AddMessageHandler<HungTaskMessage, HungTaskHandler>();

        var sp = services.BuildServiceProvider();
        var dispatcher = sp.GetRequiredService<IMessageDispatcher>();
        var serializer = sp.GetRequiredService<IMessageSerializer>();

        var msg = new HungTaskMessage(Guid.NewGuid());
        var payload = serializer.Serialize(msg);
        var metadata = TransportMessageMetadata.Create("chaos.hung-task.v1");

        using var scope = sp.CreateScope();

        // Act: Dispatch hung message
        var result = await dispatcher.DispatchAsync(
            "chaos.hung-task.v1",
            payload,
            metadata,
            scope.ServiceProvider,
            CancellationToken.None);

        // Assert: Timeout middleware caught the hang and returned a functional failure
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Messaging.Handler.Timeout");
    }

    [MessageType("chaos.catastrophic-failure.v1")]
    public sealed record CatastrophicMessage(int Id) : IMessage;

    public sealed class CatastrophicTracker
    {
        private int _calls;
        public int CallCount => _calls;
        public void RecordCall() => Interlocked.Increment(ref _calls);
    }

    public sealed class CatastrophicHandler : IMessageHandler<CatastrophicMessage>
    {
        private readonly CatastrophicTracker _tracker;
        public CatastrophicHandler(CatastrophicTracker tracker) => _tracker = tracker;

        public ValueTask<Result> HandleAsync(
            CatastrophicMessage message,
            MessageContext context,
            CancellationToken cancellationToken = default)
        {
            _tracker.RecordCall();
            throw new InvalidOperationException("Catastrophic downstream outage!");
        }
    }

    [Fact]
    public async Task Chaos_ConsecutiveExceptions_CircuitBreakerTripsOpenAndFastFails()
    {
        var tracker = new CatastrophicTracker();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(tracker);
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddMessaging(options =>
        {
            options.AddCircuitBreaker(cb =>
            {
                cb.FailureThreshold = 3;
                cb.BreakDuration = TimeSpan.FromSeconds(5);
            });
        });
        services.AddMessageHandler<CatastrophicMessage, CatastrophicHandler>();

        var sp = services.BuildServiceProvider();
        var dispatcher = sp.GetRequiredService<IMessageDispatcher>();
        var serializer = sp.GetRequiredService<IMessageSerializer>();

        var payload = serializer.Serialize(new CatastrophicMessage(1));
        var metadata = TransportMessageMetadata.Create("chaos.catastrophic-failure.v1");

        using var scope = sp.CreateScope();

        // Trip the breaker: 3 failures
        for (int i = 0; i < 3; i++)
        {
            var r = await dispatcher.DispatchAsync("chaos.catastrophic-failure.v1", payload, metadata, scope.ServiceProvider);
            r.IsFailure.Should().BeTrue();
        }

        tracker.CallCount.Should().Be(3);

        // 4th call: Circuit Breaker must be Open and fast-fail without invoking handler
        var fastFailResult = await dispatcher.DispatchAsync("chaos.catastrophic-failure.v1", payload, metadata, scope.ServiceProvider);
        fastFailResult.IsFailure.Should().BeTrue();
        fastFailResult.Error.Code.Should().Be("Messaging.CircuitBreaker.Open");
        tracker.CallCount.Should().Be(3); // Handler was NOT invoked!
    }
}
