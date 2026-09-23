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
    public void Constructor_InitializesCleanupTimer()
    {
        using var store = new InMemoryMessageDeduplicationStore();
        var timerField = typeof(InMemoryMessageDeduplicationStore).GetField("_cleanupTimer", BindingFlags.NonPublic | BindingFlags.Instance);
        timerField!.GetValue(store).Should().NotBeNull();
    }

    private sealed class FixedTimeProvider(DateTimeOffset fixedTime) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => fixedTime;
    }

    [Fact]
    public void CleanupExpiredEntries_WhenExpiryEqualsNow_RemovesEntry()
    {
        var fixedNow = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedNow);
        using var store = new InMemoryMessageDeduplicationStore(timeProvider);

        var entriesField = typeof(InMemoryMessageDeduplicationStore).GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance);
        var entries = (ConcurrentDictionary<string, DateTimeOffset>)entriesField!.GetValue(store)!;

        // Entry with expiry EXACTLY equal to now: under <= now it MUST be removed.
        // Under < now mutant it would survive.
        entries.TryAdd("exact-now", fixedNow);

        store.CleanupExpiredEntries(null);

        entries.ContainsKey("exact-now").Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenExistingExpiryEqualsNow_AllowsAcquiring()
    {
        var fixedNow = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedNow);
        using var store = new InMemoryMessageDeduplicationStore(timeProvider);

        var entriesField = typeof(InMemoryMessageDeduplicationStore).GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance);
        var entries = (ConcurrentDictionary<string, DateTimeOffset>)entriesField!.GetValue(store)!;

        // Existing expiry EXACTLY equal to now: under > now (not greater), it is considered expired and acquired!
        // Under >= now mutant, it would return false!
        entries.TryAdd("exact-now-acquire", fixedNow);

        var acquired = await store.TryAcquireAsync("exact-now-acquire", TimeSpan.FromMinutes(1));
        acquired.Should().BeTrue();
    }

    [Fact]
    public void Dispose_DisposesTimer_IsIdempotent()
    {
        var store = new InMemoryMessageDeduplicationStore();
        store.Dispose();
        store.Dispose();
    }
}
