// Copyright © Erickson Lopez. MIT License.
using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EricksonLopez.Messaging.Transport.Kafka;

using AppMessageMetadata = EricksonLopez.Messaging.Contracts.TransportMessageMetadata;
using AppResult = EricksonLopez.Result.Result;

/// <summary>
/// Provides an Apache Kafka message transport driver implementing <see cref="IMessageTransport"/>.
/// </summary>
public sealed class KafkaMessageTransport : IMessageTransport, IAsyncDisposable, IDisposable
{
    private readonly KafkaTransportOptions _options;
    private readonly ILogger<KafkaMessageTransport> _logger;
    private readonly IProducer<string, byte[]> _producer;
    private readonly Func<ConsumerConfig, IConsumer<string, byte[]>> _consumerFactory;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _subscriptions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task> _backgroundTasks = new(StringComparer.Ordinal);
    internal ConcurrentDictionary<TopicPartition, PartitionOffsetTracker> PartitionTrackers { get; } = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaMessageTransport"/> class with the specified options, producer, consumer factory, and logger.
    /// </summary>
    /// <param name="options">The Kafka transport configuration options, if specified.</param>
    /// <param name="producer">The explicit Kafka producer instance, if specified.</param>
    /// <param name="consumerFactory">The factory for creating Kafka consumer instances, if specified.</param>
    /// <param name="logger">The logger instance, if specified.</param>
    public KafkaMessageTransport(
        IOptions<KafkaTransportOptions>? options = null,
        IProducer<string, byte[]>? producer = null,
        Func<ConsumerConfig, IConsumer<string, byte[]>>? consumerFactory = null,
        ILogger<KafkaMessageTransport>? logger = null)
    {
        _options = options?.Value ?? new KafkaTransportOptions();
        _logger = logger ?? NullLogger<KafkaMessageTransport>.Instance;
        _consumerFactory = consumerFactory ?? (config => new ConsumerBuilder<string, byte[]>(config).Build());

        if (producer is not null)
        {
            _producer = producer;
        }
        else
        {
            var config = new ProducerConfig
            {
                BootstrapServers = _options.BootstrapServers,
                ClientId = _options.ClientId
            };
            _producer = new ProducerBuilder<string, byte[]>(config).Build();
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="destination"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/></exception>
    /// <exception cref="ObjectDisposedException">The transport has been disposed</exception>
    public async ValueTask<AppResult> PublishRawAsync(
        string destination,
        ReadOnlyMemory<byte> payload,
        AppMessageMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(metadata);

        try
        {
            var headers = new Headers();
            if (!string.IsNullOrWhiteSpace(metadata.MessageId))
            {
                headers.Add("message-id", Encoding.UTF8.GetBytes(metadata.MessageId));
            }
            if (!string.IsNullOrWhiteSpace(metadata.MessageType))
            {
                headers.Add("message-type", Encoding.UTF8.GetBytes(metadata.MessageType));
            }
            if (!string.IsNullOrWhiteSpace(metadata.CorrelationId))
            {
                headers.Add("correlation-id", Encoding.UTF8.GetBytes(metadata.CorrelationId));
            }
            if (!string.IsNullOrWhiteSpace(metadata.TenantId))
            {
                headers.Add("tenant-id", Encoding.UTF8.GetBytes(metadata.TenantId));
            }
            if (!string.IsNullOrWhiteSpace(metadata.TraceParent))
            {
                headers.Add("traceparent", Encoding.UTF8.GetBytes(metadata.TraceParent));
            }
            if (!string.IsNullOrWhiteSpace(metadata.CausationId))
            {
                headers.Add("causation-id", Encoding.UTF8.GetBytes(metadata.CausationId));
            }
            if (!string.IsNullOrWhiteSpace(metadata.ContentType))
            {
                headers.Add("content-type", Encoding.UTF8.GetBytes(metadata.ContentType));
            }
            if (metadata.SchemaVersion > 1)
            {
                headers.Add("schema-version", Encoding.UTF8.GetBytes(metadata.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
            if (metadata.Headers is not null)
            {
                foreach (var (key, value) in metadata.Headers)
                {
                    headers.Add(key, Encoding.UTF8.GetBytes(value));
                }
            }

            byte[] valueBytes;
            if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(payload, out var segment) &&
                segment.Count == segment.Array!.Length)
            {
                valueBytes = segment.Array;
            }
            else
            {
                valueBytes = payload.ToArray();
            }

            var messageKey = metadata.PartitionKey;
            if (string.IsNullOrWhiteSpace(messageKey))
            {
                messageKey = !string.IsNullOrWhiteSpace(metadata.MessageId) ? metadata.MessageId : Guid.NewGuid().ToString("N");
            }

            var kafkaMessage = new Message<string, byte[]>
            {
                Key = messageKey,
                Value = valueBytes,
                Headers = headers
            };

            var deliveryReport = await _producer.ProduceAsync(destination, kafkaMessage, cancellationToken);
            if (deliveryReport.Status == PersistenceStatus.NotPersisted)
            {
                return AppResult.Failure(EricksonLopez.Result.Error.Failure("Kafka.PublishFailed", $"Message was not persisted to topic {destination}."));
            }

            return AppResult.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to Kafka topic {Topic}", destination);
            return AppResult.Failure(EricksonLopez.Result.Error.Failure("Kafka.ProduceException", ex.Message));
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="destination"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentNullException"><paramref name="messageHandler"/> or <paramref name="options"/> is <see langword="null"/></exception>
    /// <exception cref="ObjectDisposedException">The transport has been disposed</exception>
    public ValueTask<AppResult> SubscribeAsync(
        string destination,
        Func<ReadOnlyMemory<byte>, AppMessageMetadata, CancellationToken, ValueTask<TransportAckResult>> messageHandler,
        TransportSubscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(messageHandler);
        ArgumentNullException.ThrowIfNull(options);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _subscriptions[destination] = cts;

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.GroupId,
            EnableAutoCommit = _options.EnableAutoCommit,
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        var task = Task.Run(async () =>
        {
            using var consumer = _consumerFactory(consumerConfig);
            consumer.Subscribe(destination);

            int maxConcurrency = Math.Max(1, options.MaxConcurrency);
            using var semaphore = maxConcurrency > 1 ? new SemaphoreSlim(maxConcurrency, maxConcurrency) : null;
            var inFlightTasks = new ConcurrentDictionary<Task, byte>();
            var consumerSyncLock = new object();

            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    ConsumeResult<string, byte[]>? consumeResult;
                    lock (consumerSyncLock)
                    {
                        consumeResult = consumer.Consume(cts.Token);
                    }
                    if (consumeResult?.Message is not null)
                    {
                        var headersDict = new Dictionary<string, string>();
                        if (consumeResult.Message.Headers is not null)
                        {
                            foreach (var h in consumeResult.Message.Headers)
                            {
                                if (h.GetValueBytes() is { } bytes)
                                {
                                    headersDict[h.Key] = Encoding.UTF8.GetString(bytes);
                                }
                            }
                        }

                        var metadata = new AppMessageMetadata(
                            MessageId: GetHeaderValue(consumeResult.Message.Headers, "message-id") ?? Guid.NewGuid().ToString("N"),
                            MessageType: GetHeaderValue(consumeResult.Message.Headers, "message-type") ?? "KafkaMessage",
                            Timestamp: DateTimeOffset.UtcNow,
                            CorrelationId: GetHeaderValue(consumeResult.Message.Headers, "correlation-id") ?? Guid.NewGuid().ToString("N"),
                            CausationId: GetHeaderValue(consumeResult.Message.Headers, "causation-id"),
                            TraceParent: GetHeaderValue(consumeResult.Message.Headers, "traceparent"),
                            TenantId: GetHeaderValue(consumeResult.Message.Headers, "tenant-id"),
                            PartitionKey: consumeResult.Message.Key,
                            ContentType: GetHeaderValue(consumeResult.Message.Headers, "content-type") ?? "application/json",
                            SchemaVersion: int.TryParse(GetHeaderValue(consumeResult.Message.Headers, "schema-version"), System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 1,
                            Headers: headersDict);

                        if (semaphore is null)
                        {
                            var ackResult = await messageHandler(consumeResult.Message.Value, metadata, cts.Token);
                            if (ackResult == TransportAckResult.Ack && !_options.EnableAutoCommit)
                            {
                                lock (consumerSyncLock)
                                {
                                    consumer.Commit(consumeResult);
                                }
                            }
                        }
                        else
                        {
                            var tracker = PartitionTrackers.GetOrAdd(consumeResult.TopicPartition, _ => new PartitionOffsetTracker());
                            tracker.Track(consumeResult.Offset.Value);

                            await semaphore.WaitAsync(cts.Token);
                            var task = Task.Run(async () =>
                            {
                                try
                                {
                                    var ackResult = await messageHandler(consumeResult.Message.Value, metadata, cts.Token);
                                    if (ackResult == TransportAckResult.Ack && !_options.EnableAutoCommit)
                                    {
                                        var nextOffset = tracker.MarkCompleted(consumeResult.Offset.Value);
                                        if (nextOffset.HasValue)
                                        {
                                            lock (consumerSyncLock)
                                            {
                                                consumer.Commit(new[] { new TopicPartitionOffset(consumeResult.TopicPartition, new Offset(nextOffset.Value)) });
                                            }
                                        }
                                    }
                                    else if (!_options.EnableAutoCommit)
                                    {
                                        tracker.MarkFailed(consumeResult.Offset.Value);
                                    }
                                }
                                catch
                                {
                                    if (!_options.EnableAutoCommit)
                                    {
                                        tracker.MarkFailed(consumeResult.Offset.Value);
                                    }
                                }
                                finally
                                {
                                    semaphore.Release();
                                }
                            }, cts.Token);

                            inFlightTasks.TryAdd(task, 0);
                            _ = task.ContinueWith(t => inFlightTasks.TryRemove(t, out _), TaskContinuationOptions.ExecuteSynchronously);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation requested during shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Kafka stream on topic {Topic}", destination);
            }
            finally
            {
                if (!inFlightTasks.IsEmpty)
                {
                    try
                    {
                        await Task.WhenAll(inFlightTasks.Keys);
                    }
                    catch (Exception)
                    {
                        // Suppress background task faults during shutdown.
                    }
                }
                lock (consumerSyncLock)
                {
                    consumer.Close();
                }
            }
        }, cts.Token);

        _backgroundTasks[destination] = task;

        return ValueTask.FromResult(AppResult.Success());
    }

    private static string? GetHeaderValue(Headers? headers, string key)
    {
        if (headers is not null && headers.TryGetLastBytes(key, out var bytes) && bytes is not null)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        return null;
    }

    /// <summary>
    /// Asynchronously releases the resources used by this instance.
    /// </summary>
    /// <returns>A value task representing the asynchronous disposal operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var cts in _subscriptions.Values)
        {
            try
            {
                await cts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Ignore already disposed token source.
            }
        }

        try
        {
            await Task.WhenAll(_backgroundTasks.Values).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Suppress background task faults during shutdown.
        }

        foreach (var cts in _subscriptions.Values)
        {
            try
            {
                cts.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Ignore already disposed token source.
            }
        }

        _subscriptions.Clear();
        _backgroundTasks.Clear();
        PartitionTrackers.Clear();

        _producer.Dispose();
    }

    /// <summary>
    /// Releases the resources used by this instance.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var cts in _subscriptions.Values)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Ignore already disposed token source.
            }
        }
        
        // Cannot safely await or dispose CTS here due to background tasks running
        _producer.Dispose();
    }
}

