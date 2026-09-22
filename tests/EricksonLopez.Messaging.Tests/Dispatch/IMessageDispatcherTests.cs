// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Tests.Dispatch;

using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Dispatch;
using EricksonLopez.Result;
using NSubstitute;
using Xunit;

[Trait("Category", "Unit")]
public sealed class IMessageDispatcherTests
{
    private sealed class MinimalDispatcher : IMessageDispatcher
    {
        public List<MessageDispatchItem> DispatchedItems { get; } = new();
        public Func<string, ReadOnlyMemory<byte>, TransportMessageMetadata, IServiceProvider, CancellationToken, ValueTask<Result>>? Handler { get; set; }

        public ValueTask<Result> DispatchAsync(
            string messageType,
            ReadOnlyMemory<byte> payload,
            TransportMessageMetadata metadata,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            DispatchedItems.Add(new MessageDispatchItem(messageType, payload, metadata));
            if (Handler != null)
            {
                return Handler(messageType, payload, metadata, serviceProvider, cancellationToken);
            }
            return ValueTask.FromResult(Result.Success());
        }
    }

    [Fact]
    public async Task DispatchBatchAsync_NullItems_ThrowsArgumentNullException()
    {
        IMessageDispatcher dispatcher = new MinimalDispatcher();
        var sp = Substitute.For<IServiceProvider>();

        Func<Task> act = async () => await dispatcher.DispatchBatchAsync(null!, sp);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("items");
    }

    [Fact]
    public async Task DispatchBatchAsync_NullServiceProvider_ThrowsArgumentNullException()
    {
        IMessageDispatcher dispatcher = new MinimalDispatcher();
        var items = new List<MessageDispatchItem>();

        Func<Task> act = async () => await dispatcher.DispatchBatchAsync(items, null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public async Task DispatchBatchAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        IMessageDispatcher dispatcher = new MinimalDispatcher();
        var sp = Substitute.For<IServiceProvider>();
        var items = new List<MessageDispatchItem>
        {
            new("type-1", new byte[] { 1 }, TransportMessageMetadata.Create("type-1"))
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await dispatcher.DispatchBatchAsync(items, sp, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DispatchBatchAsync_WhenItemFails_ReturnsFailureImmediately()
    {
        var dispatcher = new MinimalDispatcher();
        var sp = Substitute.For<IServiceProvider>();
        var items = new List<MessageDispatchItem>
        {
            new("type-1", new byte[] { 1 }, TransportMessageMetadata.Create("type-1")),
            new("type-2", new byte[] { 2 }, TransportMessageMetadata.Create("type-2")),
            new("type-3", new byte[] { 3 }, TransportMessageMetadata.Create("type-3")),
        };

        dispatcher.Handler = (type, payload, meta, s, ct) =>
        {
            if (type == "type-2")
            {
                return ValueTask.FromResult(Result.Failure(Error.Failure("Err", "Item 2 failed")));
            }
            return ValueTask.FromResult(Result.Success());
        };

        IMessageDispatcher iface = dispatcher;
        var result = await iface.DispatchBatchAsync(items, sp);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Err");
        dispatcher.DispatchedItems.Count.Should().Be(2, "batch dispatch must halt immediately upon failure");
    }

    [Fact]
    public async Task DispatchBatchAsync_WhenAllSucceed_ReturnsSuccess()
    {
        var dispatcher = new MinimalDispatcher();
        var sp = Substitute.For<IServiceProvider>();
        var items = new List<MessageDispatchItem>
        {
            new("type-1", new byte[] { 1 }, TransportMessageMetadata.Create("type-1")),
            new("type-2", new byte[] { 2 }, TransportMessageMetadata.Create("type-2")),
        };

        IMessageDispatcher iface = dispatcher;
        var result = await iface.DispatchBatchAsync(items, sp);

        result.IsSuccess.Should().BeTrue();
        dispatcher.DispatchedItems.Count.Should().Be(2);
    }
}
