// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Contracts;

namespace EricksonLopez.Messaging.Middleware;

/// <summary>
/// Provides a high-performance in-memory, thread-safe implementation of <see cref="IMessageDeduplicationStore"/>.
/// </summary>
public sealed class InMemoryMessageDeduplicationStore : IMessageDeduplicationStore, IDisposable
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _entries = new(StringComparer.Ordinal);
    private readonly Timer _cleanupTimer;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryMessageDeduplicationStore"/> class and starts the background cleanup timer.
    /// </summary>
    public InMemoryMessageDeduplicationStore() : this(TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryMessageDeduplicationStore"/> class with a custom time provider.
    /// </summary>
    /// <param name="timeProvider">The time provider instance.</param>
    public InMemoryMessageDeduplicationStore(TimeProvider? timeProvider)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
    }

    internal void CleanupExpiredEntries(object? state)
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var kvp in _entries)
        {
            if (kvp.Value <= now)
            {
                _entries.TryRemove(kvp.Key, out _);
            }
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="messageId"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public ValueTask<bool> TryAcquireAsync(string messageId, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.Add(expiration);

        while (true)
        {
            if (_entries.TryGetValue(messageId, out var existingExpiry))
            {
                if (existingExpiry > now)
                {
                    // Active entry exists: duplicate message!
                    return ValueTask.FromResult(false);
                }

                // Expired entry: attempt atomic update
                if (_entries.TryUpdate(messageId, expiresAt, existingExpiry))
                {
                    return ValueTask.FromResult(true);
                }
            }
            else
            {
                if (_entries.TryAdd(messageId, expiresAt))
                {
                    return ValueTask.FromResult(true);
                }
            }
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="messageId"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public ValueTask ReleaseAsync(string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        _entries.TryRemove(messageId, out _);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Releases the resources used by this instance.
    /// </summary>
    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}
