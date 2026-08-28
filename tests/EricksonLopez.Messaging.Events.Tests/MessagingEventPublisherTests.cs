// Copyright © Erickson Lopez. MIT License.
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Events.Contracts;
using EricksonLopez.Events.Identifiers;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Messaging.Events.Tests;

[Trait("Category", "Unit")]
public sealed class MessagingEventPublisherTests
{
    private readonly IMessagePublisher _messagePublisher = Substitute.For<IMessagePublisher>();

    private sealed record CustomerCreatedEvent(string CustomerId, string Email) : IIntegrationEvent
    {
        public EventId Id { get; init; } = EventId.New();
        public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    }

    [Fact]
    public void Constructor_WhenMessagePublisherNull_ThrowsArgumentNullException()
    {
        var act = () => new MessagingEventPublisher(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("messagePublisher");
    }

    [Fact]
    public async Task Constructor_WhenOptionsNull_InitializesDefaultOptionsWithThrowOnFailureTrue()
    {
        _messagePublisher.PublishAsync(Arg.Any<CustomerCreatedEvent>(), Arg.Any<MessagePublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<EricksonLopez.Result.Result>(EricksonLopez.Result.Result.Failure(Error.Failure("Err", "Fail"))));

        var publisher = new MessagingEventPublisher(_messagePublisher, null);
        var @event = new CustomerCreatedEvent("cust-1", "Alice");

        Func<Task> act = async () => await publisher.PublishAsync(@event);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task PublishAsync_WhenEventNull_ThrowsArgumentNullException()
    {
        var sut = new MessagingEventPublisher(_messagePublisher);
        Func<Task> act = async () => await sut.PublishAsync<CustomerCreatedEvent>(null!);
        await act.Should().ThrowExactlyAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishAsync_WhenSuccessful_PublishesToMessagePublisher()
    {
        _messagePublisher.PublishAsync(
            Arg.Any<CustomerCreatedEvent>(),
            Arg.Any<MessagePublishOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Result.Success()));

        var sut = new MessagingEventPublisher(_messagePublisher);
        var evt = new CustomerCreatedEvent("CUST-1", "test@domain.com");

        await sut.PublishAsync(evt);

        await _messagePublisher.Received(1).PublishAsync(
            evt,
            Arg.Is<MessagePublishOptions?>(o => o != null && o.Destination == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_WithDestinationResolver_SetsDestination()
    {
        _messagePublisher.PublishAsync(
            Arg.Any<CustomerCreatedEvent>(),
            Arg.Any<MessagePublishOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Result.Success()));

        var options = new MessagingEventsOptions
        {
            DestinationResolver = t => $"events.{t.Name.ToLowerInvariant()}"
        };

        var sut = new MessagingEventPublisher(_messagePublisher, options);
        var evt = new CustomerCreatedEvent("CUST-1", "test@domain.com");

        await sut.PublishAsync(evt);

        await _messagePublisher.Received(1).PublishAsync(
            evt,
            Arg.Is<MessagePublishOptions?>(opt => opt != null && opt.Destination == "events.customercreatedevent"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_WhenFailureAndThrowOnFailureTrue_ThrowsInvalidOperationExceptionWithExactMessage()
    {
        var error = Error.Failure("Transport.Failed", "Broker unavailable");
        _messagePublisher.PublishAsync(
            Arg.Any<CustomerCreatedEvent>(),
            Arg.Any<MessagePublishOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Result.Failure(error)));

        var options = new MessagingEventsOptions { ThrowOnFailure = true };
        var sut = new MessagingEventPublisher(_messagePublisher, options);
        var evt = new CustomerCreatedEvent("CUST-1", "test@domain.com");

        Func<Task> act = async () => await sut.PublishAsync(evt);

        var ex = await act.Should().ThrowExactlyAsync<InvalidOperationException>();
        ex.WithMessage($"Failed to publish event '{nameof(CustomerCreatedEvent)}' ({evt.Id}) via messaging publisher. Error: {error}");
    }

    [Fact]
    public async Task PublishAsync_WhenFailureAndThrowOnFailureFalse_DoesNotThrow()
    {
        var error = Error.Failure("Transport.Failed", "Broker unavailable");
        _messagePublisher.PublishAsync(
            Arg.Any<CustomerCreatedEvent>(),
            Arg.Any<MessagePublishOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(Result.Result.Failure(error)));

        var options = new MessagingEventsOptions { ThrowOnFailure = false };
        var sut = new MessagingEventPublisher(_messagePublisher, options);
        var evt = new CustomerCreatedEvent("CUST-1", "test@domain.com");

        Func<Task> act = async () => await sut.PublishAsync(evt);

        await act.Should().NotThrowAsync();
    }
}
