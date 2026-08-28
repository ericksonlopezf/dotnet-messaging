// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Testing;
using Xunit;

namespace EricksonLopez.Messaging.Testing.Tests;

[Trait("Category", "Unit")]
public class MessageListTests
{
    private static TransportMessageMetadata CreateMeta(string type) =>
        new(
            MessageId: Guid.NewGuid().ToString("N"),
            MessageType: type,
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: Guid.NewGuid().ToString("N"),
            CausationId: null,
            TraceParent: null,
            TenantId: null,
            PartitionKey: null,
            ContentType: "application/json",
            SchemaVersion: 1,
            Headers: new Dictionary<string, string>());

    [Fact]
    public void PublishedMessageList_Operations_WorkAsExpected()
    {
        var msg1 = new PublishedMessage("q1", new byte[] { 1 }, CreateMeta("TypeA"));
        var msg2 = new PublishedMessage("q2", new byte[] { 2 }, CreateMeta("TypeA"));
        var msg3 = new PublishedMessage("q1", new byte[] { 3 }, CreateMeta("TypeB"));

        var list = new PublishedMessageList(new[] { msg1, msg2, msg3 });

        list.Contains("TypeA").Should().BeTrue();
        list.Contains("TypeB").Should().BeTrue();
        list.Contains("TypeC").Should().BeFalse();

        list.OfType("TypeA").Should().HaveCount(2);
        list.OfType("TypeB").Should().HaveCount(1);
        list.OfType("TypeC").Should().BeEmpty();

        list.Count.Should().Be(3);
        list[0].Should().Be(msg1);
        list[1].Should().Be(msg2);
        list[2].Should().Be(msg3);

        // Enumerable
        var all = list.ToList();
        all.Should().HaveCount(3);

        var nonGenericEnumerator = ((System.Collections.IEnumerable)list).GetEnumerator();
        nonGenericEnumerator.Should().NotBeNull();
        nonGenericEnumerator.MoveNext().Should().BeTrue();
    }

    [Fact]
    public void ConsumedMessageList_Operations_WorkAsExpected()
    {
        var msg1 = new ConsumedMessage("q1", new byte[] { 1 }, CreateMeta("OrderPlaced"), Succeeded: true);
        var msg2 = new ConsumedMessage("q1", new byte[] { 2 }, CreateMeta("OrderPlaced"), Succeeded: false);
        var msg3 = new ConsumedMessage("q2", new byte[] { 3 }, CreateMeta("PaymentFailed"), Succeeded: false);

        var list = new ConsumedMessageList(new[] { msg1, msg2, msg3 });

        list.Count.Should().Be(3);
        list[0].Should().Be(msg1);
        list[1].Should().Be(msg2);
        list[2].Should().Be(msg3);

        list.Contains("OrderPlaced").Should().BeTrue();
        list.Contains("PaymentFailed").Should().BeTrue();
        list.Contains("RefundDone").Should().BeFalse();

        list.OfType("OrderPlaced").Should().HaveCount(2);
        list.OfType("PaymentFailed").Should().HaveCount(1);
        list.OfType("RefundDone").Should().BeEmpty();

        list.AnySucceeded("OrderPlaced").Should().BeTrue();
        list.AnySucceeded("PaymentFailed").Should().BeFalse();
        list.AnySucceeded("Unknown").Should().BeFalse();

        // Enumerable
        var all = list.ToList();
        all.Should().HaveCount(3);

        var nonGenericEnumerator = ((System.Collections.IEnumerable)list).GetEnumerator();
        nonGenericEnumerator.Should().NotBeNull();
        nonGenericEnumerator.MoveNext().Should().BeTrue();
    }

    [Fact]
    public void PublishedMessageAndConsumedMessage_Records_SupportEquality()
    {
        var meta = CreateMeta("Test");
        var payload = new byte[] { 1, 2, 3 };

        var pub1 = new PublishedMessage("dest", payload, meta);
        var pub2 = new PublishedMessage("dest", payload, meta);
        pub1.Should().Be(pub2);

        var con1 = new ConsumedMessage("dest", payload, meta, true);
        var con2 = new ConsumedMessage("dest", payload, meta, true);
        con1.Should().Be(con2);
    }
}

