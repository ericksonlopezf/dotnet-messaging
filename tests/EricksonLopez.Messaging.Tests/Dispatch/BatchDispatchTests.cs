// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Dispatch;
using EricksonLopez.Messaging.Serialization;
namespace EricksonLopez.Messaging.Tests.Dispatch;

using Microsoft.Extensions.DependencyInjection;
using Result = EricksonLopez.Result.Result;
using Xunit;

public sealed class BatchDispatchTests
{
    [MessageType("batch.item.test")]
    public sealed record BatchItem(int Index, string Name) : IMessage;

    public sealed class BatchItemHandler : IMessageHandler<BatchItem>
    {
        public List<int> HandledIndices { get; } = new();

        public ValueTask<Result> HandleAsync(BatchItem message, MessageContext context, CancellationToken cancellationToken)
        {
            HandledIndices.Add(message.Index);
            return ValueTask.FromResult(Result.Success());
        }
    }

    [Fact]
    public async Task DispatchBatchAsync_AllValidItems_ExecutesAllHandlersSuccessfully()
    {
        var services = new ServiceCollection();
        var handler = new BatchItemHandler();
        services.AddSingleton(handler);
        services.AddScoped<BatchItemHandler>(_ => handler);
        services.AddScoped<IMessageHandler<BatchItem>>(_ => handler);

        var serializer = new NativeAotJsonSerializer(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddSingleton<IMessageSerializer>(serializer);

        var dispatcher = new DefaultMessageDispatcher(serializer);
        dispatcher.RegisterHandler<BatchItem, BatchItemHandler>("batch.item.test");

        using var sp = services.BuildServiceProvider();

        var items = new List<MessageDispatchItem>();
        for (int i = 1; i <= 5; i++)
        {
            var msg = new BatchItem(i, $"Item-{i}");
            var payload = serializer.Serialize(msg);
            var metadata = TransportMessageMetadata.Create("batch.item.test") with { MessageId = $"msg-batch-{i}" };
            items.Add(new MessageDispatchItem("batch.item.test", payload, metadata));
        }

        var result = await dispatcher.DispatchBatchAsync(items, sp, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        handler.HandledIndices.Should().HaveCount(5);
        handler.HandledIndices.Should().ContainInOrder(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task DispatchBatchAsync_WithUnknownItem_ReturnsFailureAndStops()
    {
        var services = new ServiceCollection();
        var handler = new BatchItemHandler();
        services.AddSingleton(handler);
        services.AddScoped<BatchItemHandler>(_ => handler);
        services.AddScoped<IMessageHandler<BatchItem>>(_ => handler);

        var serializer = new NativeAotJsonSerializer(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        var dispatcher = new DefaultMessageDispatcher(serializer);
        dispatcher.RegisterHandler<BatchItem, BatchItemHandler>("batch.item.test");

        using var sp = services.BuildServiceProvider();

        var validMsg = new BatchItem(1, "Valid");
        var items = new List<MessageDispatchItem>
        {
            new("batch.item.test", serializer.Serialize(validMsg), TransportMessageMetadata.Create("batch.item.test")),
            new("unknown.type", new byte[] { 1, 2, 3 }, TransportMessageMetadata.Create("unknown.type"))
        };

        var result = await dispatcher.DispatchBatchAsync(items, sp, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Messaging.BatchPartialFailure");
        handler.HandledIndices.Should().HaveCount(1);
    }
}
