// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EricksonLopez.Messaging.Tests.Contracts;

using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;

public sealed class DeadLetterQueueTests
{
    [Fact]
    public void DeadLetterReason_FromException_WithException_ConstructsValidRecord()
    {
        Exception ex;
        try
        {
            throw new InvalidOperationException("Failed to decode byte stream at offset 42.");
        }
        catch (Exception caught)
        {
            ex = caught;
        }

        var reason = DeadLetterReason.FromException("DESERIALIZATION_FAILURE", "Malformed payload", ex);

        Assert.Equal("DESERIALIZATION_FAILURE", reason.ReasonCode);
        Assert.Equal("Malformed payload", reason.Description);
        Assert.Equal(typeof(InvalidOperationException).FullName, reason.ExceptionType);
        Assert.NotNull(reason.StackTrace);
        Assert.True(reason.OccurredAtUtc <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void DeadLetterReason_FromException_WithNullException_SetsNullTypeAndStackTrace()
    {
        var before = DateTimeOffset.UtcNow;
        var reason = DeadLetterReason.FromException("POISON_PAYLOAD", "Payload corrupted without exception", null);
        var after = DateTimeOffset.UtcNow;

        Assert.Equal("POISON_PAYLOAD", reason.ReasonCode);
        Assert.Equal("Payload corrupted without exception", reason.Description);
        Assert.Null(reason.ExceptionType);
        Assert.Null(reason.StackTrace);
        Assert.True(reason.OccurredAtUtc >= before && reason.OccurredAtUtc <= after);
    }

    [Fact]
    public void DeadLetterReason_FromException_WithDefaultOptionalException_SetsNullTypeAndStackTrace()
    {
        var reason = DeadLetterReason.FromException("TIMEOUT_EXCEEDED", "Handler timed out");

        Assert.Equal("TIMEOUT_EXCEEDED", reason.ReasonCode);
        Assert.Equal("Handler timed out", reason.Description);
        Assert.Null(reason.ExceptionType);
        Assert.Null(reason.StackTrace);
    }

    [Fact]
    public void DeadLetterReason_ConstructorAndRecordSemantics_WorkCorrectly()
    {
        var timestamp = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
        var reason = new DeadLetterReason(
            ReasonCode: "CUSTOM_CODE",
            Description: "Custom description",
            ExceptionType: "System.Exception",
            StackTrace: "at Test.Method()",
            OccurredAtUtc: timestamp);

        Assert.Equal("CUSTOM_CODE", reason.ReasonCode);
        Assert.Equal("Custom description", reason.Description);
        Assert.Equal("System.Exception", reason.ExceptionType);
        Assert.Equal("at Test.Method()", reason.StackTrace);
        Assert.Equal(timestamp, reason.OccurredAtUtc);

        var mutated = reason with { ReasonCode = "NEW_CODE" };
        Assert.Equal("NEW_CODE", mutated.ReasonCode);
        Assert.Equal("Custom description", mutated.Description);
    }

    [Fact]
    public async Task ForwardToDeadLetterAsync_ValidReason_RoutesSuccessfully()
    {
        var dlq = new InMemoryDeadLetterQueue();
        var reason = new DeadLetterReason("POISON_MESSAGE", "Invalid message schema");

        var result = await dlq.ForwardToDeadLetterAsync("test-message", reason);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, dlq.ForwardedCount);
    }

    [Fact]
    public async Task ForwardRawToDeadLetterAsync_ValidPayloadAndMetadata_RoutesSuccessfully()
    {
        var dlq = new InMemoryDeadLetterQueue();
        var reason = new DeadLetterReason("RAW_PAYLOAD_CORRUPT", "Bytes corrupted");
        var metadata = TransportMessageMetadata.Create("raw.type");
        var payload = new byte[] { 0x01, 0x02, 0x03 };

        var result = await dlq.ForwardRawToDeadLetterAsync(payload, reason, metadata);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, dlq.ForwardedCount);
    }

    private sealed class InMemoryDeadLetterQueue : IDeadLetterQueue
    {
        public int ForwardedCount { get; private set; }

        public ValueTask<Result> ForwardToDeadLetterAsync<TMessage>(
            TMessage message,
            DeadLetterReason reason,
            MessageContext? context = null,
            CancellationToken cancellationToken = default) where TMessage : notnull
        {
            ForwardedCount++;
            return ValueTask.FromResult(Result.Success());
        }

        public ValueTask<Result> ForwardRawToDeadLetterAsync(
            ReadOnlyMemory<byte> rawPayload,
            DeadLetterReason reason,
            TransportMessageMetadata? metadata = null,
            CancellationToken cancellationToken = default)
        {
            ForwardedCount++;
            return ValueTask.FromResult(Result.Success());
        }
    }
}
