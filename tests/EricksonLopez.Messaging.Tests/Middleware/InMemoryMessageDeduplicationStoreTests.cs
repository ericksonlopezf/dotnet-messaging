// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Messaging.Middleware;
using Xunit;

namespace EricksonLopez.Messaging.Tests.Middleware;

[Trait("Category", "Unit")]
public sealed class InMemoryMessageDeduplicationStoreTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TryAcquireAsync_NullOrWhitespaceMessageId_ThrowsArgumentException(string? messageId)
    {
        using var store = new InMemoryMessageDeduplicationStore();
        Func<Task> act = async () => await store.TryAcquireAsync(messageId!, TimeSpan.FromMinutes(1));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReleaseAsync_NullOrWhitespaceMessageId_ThrowsArgumentException(string? messageId)
    {
        using var store = new InMemoryMessageDeduplicationStore();
        Func<Task> act = async () => await store.ReleaseAsync(messageId!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TryAcquireAsync_FirstTime_ReturnsTrue()
    {
        using var store = new InMemoryMessageDeduplicationStore();
        var acquired = await store.TryAcquireAsync("msg-01", TimeSpan.FromMinutes(1));
        acquired.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_ActiveEntry_ReturnsFalse()
    {
        using var store = new InMemoryMessageDeduplicationStore();
        await store.TryAcquireAsync("msg-02", TimeSpan.FromMinutes(1));
        var duplicate = await store.TryAcquireAsync("msg-02", TimeSpan.FromMinutes(1));
        duplicate.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireAsync_ExpiredEntry_AllowsReAcquire()
    {
        using var store = new InMemoryMessageDeduplicationStore();
        await store.TryAcquireAsync("msg-03", TimeSpan.FromMilliseconds(10));
        await Task.Delay(30);

        var reacquired = await store.TryAcquireAsync("msg-03", TimeSpan.FromMinutes(1));
        reacquired.Should().BeTrue();
    }

    [Fact]
    public async Task ReleaseAsync_RemovesEntry_AllowsImmediateReAcquire()
    {
        using var store = new InMemoryMessageDeduplicationStore();
        await store.TryAcquireAsync("msg-04", TimeSpan.FromMinutes(1));
        await store.ReleaseAsync("msg-04");

        var acquiredAgain = await store.TryAcquireAsync("msg-04", TimeSpan.FromMinutes(1));
        acquiredAgain.Should().BeTrue();
    }

    [Fact]
    public void CleanupExpiredEntries_RemovesOnlyExpiredEntries()
    {
        using var store = new InMemoryMessageDeduplicationStore();
        var entriesField = typeof(InMemoryMessageDeduplicationStore).GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance);
        var entries = (ConcurrentDictionary<string, DateTimeOffset>)entriesField!.GetValue(store)!;

        entries.TryAdd("expired-1", DateTimeOffset.UtcNow.AddSeconds(-20));
        entries.TryAdd("expired-2", DateTimeOffset.UtcNow.AddSeconds(-5));
        entries.TryAdd("active-1", DateTimeOffset.UtcNow.AddSeconds(120));

        var cleanupMethod = typeof(InMemoryMessageDeduplicationStore).GetMethod("CleanupExpiredEntries", BindingFlags.NonPublic | BindingFlags.Instance);
        cleanupMethod!.Invoke(store, [null]);

        entries.ContainsKey("expired-1").Should().BeFalse();
        entries.ContainsKey("expired-2").Should().BeFalse();
        entries.ContainsKey("active-1").Should().BeTrue();
    }

    [Fact]
    public void Dispose_DisposesTimer_IsIdempotent()
    {
        var store = new InMemoryMessageDeduplicationStore();
        store.Dispose();
        store.Dispose();
    }
}
