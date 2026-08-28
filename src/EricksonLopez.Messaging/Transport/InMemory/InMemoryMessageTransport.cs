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
public sealed class InMemoryMessageTransport : IDeferableMessageTransport, IBatchMessageTransport, IAsyncDisposable, IDisposable
{
    private readonly InMemoryTransportOptions _options;
    private readonly ILogger<InMemoryMessageTransport> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, List<SubscriptionEntry>> _subscriptions = new(StringComparer.Ordinal);
    private bool _disposed;

    private readonly record struct InMemoryPacket(
        ReadOnlyMemory<byte> Payload,
        TransportMessageMetadata Metadata);

    private sealed class SubscriptionEntry
    {
        public Channel<InMemoryPacket> Channel { get; }
        public CancellationTokenSource LoopCts { get; } = new();

        public SubscriptionEntry(Channel<InMemoryPacket> channel)
        {
            Channel = channel;
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
            if (!entry.Channel.Writer.TryWrite(packet))
            {
                await entry.Channel.Writer.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
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
                if (!entry.Channel.Writer.TryWrite(packet))
                {
                    await entry.Channel.Writer.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
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

        var channel = Channel.CreateBounded<InMemoryPacket>(channelOptions);
        var entry = new SubscriptionEntry(channel);

        var list = _subscriptions.GetOrAdd(destination, _ => new List<SubscriptionEntry>());
        lock (list)
        {
            list.Add(entry);
        }

        var semaphore = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);
        _ = Task.Run(() => RunSubscriptionLoopAsync(entry, messageHandler, semaphore), entry.LoopCts.Token);

        return ValueTask.FromResult(Result.Success());
    }

    private async Task RunSubscriptionLoopAsync(
        SubscriptionEntry entry,
        Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>> messageHandler,
        SemaphoreSlim semaphore)
    {
        var reader = entry.Channel.Reader;
        var loopCt = entry.LoopCts.Token;

        while (!loopCt.IsCancellationRequested)
        {
            try
            {
                await semaphore.WaitAsync(loopCt).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            InMemoryPacket packet;
            try
            {
                packet = await reader.ReadAsync(loopCt).ConfigureAwait(false);
            }
            catch (Exception)
            {
                semaphore.Release();
                break;
            }

            _ = Task.Run(() => ProcessMessageAsync(entry, messageHandler, semaphore, packet, loopCt), loopCt);
        }
    }

    private async Task ProcessMessageAsync(
        SubscriptionEntry entry,
        Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>> messageHandler,
        SemaphoreSlim semaphore,
        InMemoryPacket packet,
        CancellationToken loopCt)
    {
        try
        {
            var ackResult = await messageHandler(packet.Payload, packet.Metadata, loopCt).ConfigureAwait(false);
            if (ackResult == TransportAckResult.NackRequeue)
            {
                await entry.Channel.Writer.WriteAsync(packet, loopCt).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected error processing in-memory message '{MessageId}'", packet.Metadata.MessageId);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
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
                    entry.Channel.Writer.TryComplete();
                    entry.LoopCts.Cancel();
                    entry.LoopCts.Dispose();
                }
                list.Clear();
            }
        }

        _subscriptions.Clear();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _ = DisposeAsync();
    }
}

