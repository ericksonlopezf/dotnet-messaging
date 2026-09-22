// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Transport.InMemory;

using System.Collections.Concurrent;
using System.Threading.Channels;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

/// <summary>
/// Provides an in-memory message transport backed by channels for local development and integration testing.
/// </summary>
public sealed class InMemoryMessageTransport : IDeferableMessageTransport, IBatchMessageTransport, IDisposable
{
    private readonly InMemoryTransportOptions _options;
    private readonly ILogger<InMemoryMessageTransport> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, List<SubscriptionEntry>> _subscriptions = new(StringComparer.Ordinal);
    private long _roundRobinCounter = -1;
    private bool _disposed;

    private readonly record struct InMemoryPacket(
        ReadOnlyMemory<byte> Payload,
        TransportMessageMetadata Metadata);

    private sealed class SubscriptionEntry
    {
        public Channel<InMemoryPacket>[] Channels { get; }
        public CancellationTokenSource LoopCts { get; } = new();

        public SubscriptionEntry(Channel<InMemoryPacket>[] channels)
        {
            Channels = channels;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryMessageTransport"/> class with the specified options, logger, and time provider.
    /// </summary>
    /// <param name="options">The transport configuration options.</param>
    /// <param name="logger">The logger instance, if specified.</param>
    /// <param name="timeProvider">The time provider for time-based operations, if specified.</param>
    public InMemoryMessageTransport(
        IOptions<InMemoryTransportOptions>? options = null,
        ILogger<InMemoryMessageTransport>? logger = null,
        TimeProvider? timeProvider = null)
    {
        _options = options?.Value ?? new InMemoryTransportOptions();
        _logger = logger ?? NullLogger<InMemoryMessageTransport>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="destination"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/></exception>
    /// <exception cref="ObjectDisposedException">The transport has been disposed</exception>
    public async ValueTask<Result> PublishRawAsync(
        string destination,
        ReadOnlyMemory<byte> payload,
        TransportMessageMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(metadata);

        if (!_subscriptions.TryGetValue(destination, out var entries) || entries.Count == 0)
        {
            _logger.LogDebug("No active subscribers for destination '{Destination}'. Message '{MessageId}' published as unrouted.", destination, metadata.MessageId);
            return Result.Success();
        }

        var packet = new InMemoryPacket(payload, metadata);

        SubscriptionEntry[] entriesSnapshot;
        lock (entries)
        {
            entriesSnapshot = entries.ToArray();
        }

        foreach (var entry in entriesSnapshot)
        {
            var channel = entry.Channels[GetPartitionIndex(metadata.PartitionKey, entry.Channels.Length)];
            if (!channel.Writer.TryWrite(packet))
            {
                await channel.Writer.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
            }
        }

        return Result.Success();
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="destination"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is <see langword="null"/></exception>
    /// <exception cref="ObjectDisposedException">The transport has been disposed</exception>
    public async ValueTask<Result> PublishBatchRawAsync(
        string destination,
        IReadOnlyList<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)> batch,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Count == 0)
        {
            return Result.Success();
        }

        if (!_subscriptions.TryGetValue(destination, out var entries) || entries.Count == 0)
        {
            _logger.LogDebug("No active subscribers for destination '{Destination}'. Batch of {Count} messages published as unrouted.", destination, batch.Count);
            return Result.Success();
        }

        SubscriptionEntry[] entriesSnapshot;
        lock (entries)
        {
            entriesSnapshot = entries.ToArray();
        }

        foreach (var (payload, metadata) in batch)
        {
            var packet = new InMemoryPacket(payload, metadata);
            foreach (var entry in entriesSnapshot)
            {
                var channel = entry.Channels[GetPartitionIndex(metadata.PartitionKey, entry.Channels.Length)];
                if (!channel.Writer.TryWrite(packet))
                {
                    await channel.Writer.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return Result.Success();
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="destination"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/></exception>
    /// <exception cref="ObjectDisposedException">The transport has been disposed</exception>
    public async ValueTask<Result> DeferRawAsync(
        string destination,
        ReadOnlyMemory<byte> payload,
        TransportMessageMetadata metadata,
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(metadata);

        if (delay <= TimeSpan.Zero)
        {
            return await PublishRawAsync(destination, payload, metadata, cancellationToken).ConfigureAwait(false);
        }

        _ = Task.Run(() => ExecuteDeferredDeliveryAsync(destination, payload, metadata, delay, cancellationToken), cancellationToken);

        return Result.Success();
    }

    private async Task ExecuteDeferredDeliveryAsync(
        string destination,
        ReadOnlyMemory<byte> payload,
        TransportMessageMetadata metadata,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            await PublishRawAsync(destination, payload, metadata, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error during deferred delivery of message '{MessageId}' to '{Destination}'", metadata.MessageId, destination);
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="destination"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentNullException"><paramref name="messageHandler"/> is <see langword="null"/></exception>
    /// <exception cref="ObjectDisposedException">The transport has been disposed</exception>
    public ValueTask<Result> SubscribeAsync(
        string destination,
        Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>> messageHandler,
        TransportSubscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(messageHandler);

        var channelOptions = new BoundedChannelOptions(_options.ChannelCapacity)
        {
            FullMode = _options.FullMode
        };

        var partitions = Math.Max(1, options.MaxConcurrency);
        var channels = new Channel<InMemoryPacket>[partitions];
        for (int i = 0; i < partitions; i++)
        {
            channels[i] = Channel.CreateBounded<InMemoryPacket>(channelOptions);
        }
        var entry = new SubscriptionEntry(channels);

        var list = _subscriptions.GetOrAdd(destination, _ => new List<SubscriptionEntry>());
        lock (list)
        {
            list.Add(entry);
        }

        for (int i = 0; i < partitions; i++)
        {
            var channel = channels[i];
            _ = Task.Run(() => RunSubscriptionLoopAsync(channel, messageHandler, entry.LoopCts.Token), entry.LoopCts.Token);
        }

        return ValueTask.FromResult(Result.Success());
    }

    private int GetPartitionIndex(string? partitionKey, int partitionCount)
    {
        if (partitionCount <= 1) return 0;
        if (string.IsNullOrEmpty(partitionKey))
        {
            var count = Interlocked.Increment(ref _roundRobinCounter);
            return (int)(Math.Abs(count) % partitionCount);
        }
        int hash = partitionKey.GetHashCode(StringComparison.Ordinal);
        if (hash == int.MinValue) hash = 0;
        return Math.Abs(hash) % partitionCount;
    }

    private async Task RunSubscriptionLoopAsync(
        Channel<InMemoryPacket> channel,
        Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>> messageHandler,
        CancellationToken loopCt)
    {
        var reader = channel.Reader;

        while (!loopCt.IsCancellationRequested)
        {
            InMemoryPacket packet;
            try
            {
                packet = await reader.ReadAsync(loopCt).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                break;
            }

            await ProcessMessageAsync(channel, messageHandler, packet, loopCt).ConfigureAwait(false);
        }
    }

    private async Task ProcessMessageAsync(
        Channel<InMemoryPacket> channel,
        Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>> messageHandler,
        InMemoryPacket packet,
        CancellationToken loopCt)
    {
        try
        {
            var ackResult = await messageHandler(packet.Payload, packet.Metadata, loopCt).ConfigureAwait(false);
            if (ackResult == TransportAckResult.NackRequeue)
            {
                await Task.Delay(100, loopCt).ConfigureAwait(false); // Livelock prevention
                await channel.Writer.WriteAsync(packet, loopCt).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected error processing in-memory message '{MessageId}'", packet.Metadata.MessageId);
        }
    }

    /// <summary>
    /// Asynchronously releases the resources used by this instance.
    /// </summary>
    /// <returns>A value task representing the asynchronous disposal operation.</returns>
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        foreach (var list in _subscriptions.Values)
        {
            lock (list)
            {
                foreach (var entry in list)
                {
                    foreach (var channel in entry.Channels)
                    {
                        channel.Writer.TryComplete();
                    }
                    try
                    {
                        entry.LoopCts.Cancel();
                        entry.LoopCts.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Ignore already disposed token source.
                    }
                }
                list.Clear();
            }
        }

        _subscriptions.Clear();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Releases the resources used by this instance.
    /// </summary>
    public void Dispose()
    {
        _ = DisposeAsync();
    }
}

