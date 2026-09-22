// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Dispatch;
using Xunit;

namespace EricksonLopez.Messaging.Tests.Dispatch;

public sealed class PartitionKeyExtractorTests
{
    private sealed record StringKeyMessage([property: PartitionKey] string Key, string Content) : IMessage;
    private sealed record GuidKeyMessage([property: PartitionKey] Guid Key, string Content) : IMessage;
    private sealed record NullableGuidKeyMessage([property: PartitionKey] Guid? Key, string Content) : IMessage;
    private sealed record IntKeyMessage([property: PartitionKey] int Key, string Content) : IMessage;
    private sealed record LongKeyMessage([property: PartitionKey] long Key, string Content) : IMessage;
    private sealed record DateTimeOffsetKeyMessage([property: PartitionKey] DateTimeOffset Key, string Content) : IMessage;
    private sealed record NoKeyMessage(string Content) : IMessage;

    [Fact]
    public void Extract_StringKey_ReturnsExactString()
    {
        var msg = new StringKeyMessage("customer-42", "payload");
        var key = PartitionKeyExtractor<StringKeyMessage>.Extract(msg);
        key.Should().Be("customer-42");
    }

    [Fact]
    public void Extract_GuidKey_ReturnsNFormatStringWithoutBoxing()
    {
        var id = Guid.NewGuid();
        var msg = new GuidKeyMessage(id, "payload");
        var key = PartitionKeyExtractor<GuidKeyMessage>.Extract(msg);
        key.Should().Be(id.ToString("N"));
    }

    [Fact]
    public void Extract_NullableGuidKey_ReturnsFormattedOrNull()
    {
        var id = Guid.NewGuid();
        var msgWithVal = new NullableGuidKeyMessage(id, "payload");
        PartitionKeyExtractor<NullableGuidKeyMessage>.Extract(msgWithVal).Should().Be(id.ToString("N"));

        var msgNull = new NullableGuidKeyMessage(null, "payload");
        PartitionKeyExtractor<NullableGuidKeyMessage>.Extract(msgNull).Should().BeNull();
    }

    [Fact]
    public void Extract_IntKey_ReturnsInvariantCultureString()
    {
        var msg = new IntKeyMessage(12345, "payload");
        var key = PartitionKeyExtractor<IntKeyMessage>.Extract(msg);
        key.Should().Be("12345");
    }

    [Fact]
    public void Extract_LongKey_ReturnsInvariantCultureString()
    {
        var msg = new LongKeyMessage(9876543210L, "payload");
        var key = PartitionKeyExtractor<LongKeyMessage>.Extract(msg);
        key.Should().Be("9876543210");
    }

    [Fact]
    public void Extract_DateTimeOffsetKey_ReturnsIsoString()
    {
        var now = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
        var msg = new DateTimeOffsetKeyMessage(now, "payload");
        var key = PartitionKeyExtractor<DateTimeOffsetKeyMessage>.Extract(msg);
        key.Should().Be(now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    private sealed record NullableIntKeyMessage([property: PartitionKey] int? Key, string Content) : IMessage;
    private sealed record NullableLongKeyMessage([property: PartitionKey] long? Key, string Content) : IMessage;
    private sealed record DateTimeKeyMessage([property: PartitionKey] DateTime Key, string Content) : IMessage;
    private sealed class StaticPropKeyMessage : IMessage
    {
        [PartitionKey]
        public static string StaticKey => "static-val";
    }

    [Fact]
    public void Extract_NullableIntKey_ReturnsFormattedOrNull()
    {
        var msgWithVal = new NullableIntKeyMessage(42, "payload");
        PartitionKeyExtractor<NullableIntKeyMessage>.Extract(msgWithVal).Should().Be("42");

        var msgNull = new NullableIntKeyMessage(null, "payload");
        PartitionKeyExtractor<NullableIntKeyMessage>.Extract(msgNull).Should().BeNull();
    }

    [Fact]
    public void Extract_NullableLongKey_ReturnsFormattedOrNull()
    {
        var msgWithVal = new NullableLongKeyMessage(1234567890123L, "payload");
        PartitionKeyExtractor<NullableLongKeyMessage>.Extract(msgWithVal).Should().Be("1234567890123");

        var msgNull = new NullableLongKeyMessage(null, "payload");
        PartitionKeyExtractor<NullableLongKeyMessage>.Extract(msgNull).Should().BeNull();
    }

    [Fact]
    public void Extract_DateTimeKey_ReturnsIsoString()
    {
        var dt = new DateTime(2026, 9, 22, 18, 0, 0, DateTimeKind.Utc);
        var msg = new DateTimeKeyMessage(dt, "payload");
        var key = PartitionKeyExtractor<DateTimeKeyMessage>.Extract(msg);
        key.Should().Be(dt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Extract_StaticPropertyWithAttribute_ReturnsNull()
    {
        var msg = new StaticPropKeyMessage();
        var key = PartitionKeyExtractor<StaticPropKeyMessage>.Extract(msg);
        key.Should().BeNull();
    }

    [Fact]
    public void Extract_NoKeyAttribute_ReturnsNull()
    {
        var msg = new NoKeyMessage("payload");
        var key = PartitionKeyExtractor<NoKeyMessage>.Extract(msg);
        key.Should().BeNull();
    }

    private sealed class WriteOnlyKeyMessage : IMessage
    {
        private string? _val;
        [PartitionKey]
        public string Key { set => _val = value; }
        public string? Val => _val;
    }

    [Fact]
    public void Extract_WriteOnlyPropertyWithAttribute_ThrowsArgumentException()
    {
        var msg = new WriteOnlyKeyMessage();
        Action act = () => PartitionKeyExtractor<WriteOnlyKeyMessage>.Extract(msg);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Getter_StringKey_UsesOpenInstanceDelegateWithNullTarget()
    {
        var getterField = typeof(PartitionKeyExtractor<StringKeyMessage>).GetField("Getter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var getter = (Delegate)getterField!.GetValue(null)!;
        getter.Target.Should().BeNull("string partition key extractor must use a high-performance open instance delegate");
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075")]
    public void Getter_NumericKeys_UseCompiledDelegateFastPath()
    {
        var intField = typeof(PartitionKeyExtractor<IntKeyMessage>).GetField("Getter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var intGetter = (Delegate)intField!.GetValue(null)!;
        intGetter.Target.Should().NotBeNull();
        intGetter.Target!.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Should().Contain(f => f.FieldType == typeof(Func<IntKeyMessage, int>));

        var longField = typeof(PartitionKeyExtractor<LongKeyMessage>).GetField("Getter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var longGetter = (Delegate)longField!.GetValue(null)!;
        longGetter.Target.Should().NotBeNull();
        longGetter.Target!.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Should().Contain(f => f.FieldType == typeof(Func<LongKeyMessage, long>));

        var nullLongField = typeof(PartitionKeyExtractor<NullableLongKeyMessage>).GetField("Getter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var nullLongGetter = (Delegate)nullLongField!.GetValue(null)!;
        nullLongGetter.Target.Should().NotBeNull();
        nullLongGetter.Target!.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Should().Contain(f => f.FieldType == typeof(Func<NullableLongKeyMessage, long?>));
    }
}


