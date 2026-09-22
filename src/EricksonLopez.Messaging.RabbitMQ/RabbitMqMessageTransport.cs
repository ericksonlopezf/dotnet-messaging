// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Transport.RabbitMQ;

using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Result;
using global::RabbitMQ.Client;
using global::RabbitMQ.Client.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

/// <summary>
/// Provides a RabbitMQ message transport driver implementing <see cref="IMessageTransport"/>.
/// </summary>
public sealed class RabbitMqMessageTransport : IMessageTransport, IAsyncDisposable, IDisposable
{
    private readonly RabbitMqTransportOptions _options;
    private readonly ILogger<RabbitMqMessageTransport> _logger;
    private readonly IConnectionFactory _connectionFactory;
    private IConnection? _connection;
    private IChannel? _publishChannel;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMqMessageTransport"/> class with the specified options, connection factory, and logger.
    /// </summary>
    /// <param name="options">The RabbitMQ transport configuration options, if specified.</param>
    /// <param name="connectionFactory">The explicit RabbitMQ connection factory, if specified.</param>
    /// <param name="logger">The logger instance, if specified.</param>
    public RabbitMqMessageTransport(
        IOptions<RabbitMqTransportOptions>? options = null,
        IConnectionFactory? connectionFactory = null,
        ILogger<RabbitMqMessageTransport>? logger = null)
    {
        _options = options?.Value ?? new RabbitMqTransportOptions();
        _logger = logger ?? NullLogger<RabbitMqMessageTransport>.Instance;
        _connectionFactory = connectionFactory ?? new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            VirtualHost = _options.VirtualHost,
            UserName = _options.UserName,
            Password = _options.Password
        };
    }

    private async ValueTask<IChannel> GetOrCreatePublishChannelAsync(CancellationToken cancellationToken)
    {
        if (_publishChannel is not null && _publishChannel.IsOpen)
        {
            return _publishChannel;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_publishChannel is not null && _publishChannel.IsOpen)
            {
                return _publishChannel;
            }

            _connection ??= await _connectionFactory.CreateConnectionAsync(cancellationToken);
            _publishChannel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
            return _publishChannel;
        }
        finally
        {
            _initLock.Release();
        }
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

        try
        {
            var channel = await GetOrCreatePublishChannelAsync(cancellationToken);

            var props = new BasicProperties
            {
                MessageId = metadata.MessageId,
                CorrelationId = metadata.CorrelationId,
                Type = metadata.MessageType,
                ContentType = metadata.ContentType ?? "application/json"
            };

            var headers = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(metadata.TraceParent))
            {
                headers["traceparent"] = metadata.TraceParent;
            }
            if (!string.IsNullOrWhiteSpace(metadata.CausationId))
            {
                headers["causation-id"] = metadata.CausationId;
            }
            if (!string.IsNullOrWhiteSpace(metadata.TenantId))
            {
                headers["tenant-id"] = metadata.TenantId;
            }
            if (!string.IsNullOrWhiteSpace(metadata.PartitionKey))
            {
                headers["partition-key"] = metadata.PartitionKey;
            }
            if (metadata.SchemaVersion > 1)
            {
                headers["schema-version"] = metadata.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (metadata.Headers is not null)
            {
                foreach (var pair in metadata.Headers)
                {
                    headers[pair.Key] = pair.Value;
                }
            }

            if (headers.Count > 0)
            {
                props.Headers = headers;
            }

            var exchange = _options.ExchangeName;
            var routingKey = destination;

            await _publishLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await channel.BasicPublishAsync(
                    exchange: exchange,
                    routingKey: routingKey,
                    mandatory: false,
                    basicProperties: props,
                    body: payload,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _publishLock.Release();
            }

            return Result.Success();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to publish message to RabbitMQ destination '{Destination}'", destination);
            return Result.Failure(Error.Failure(
                code: "RabbitMQ.PublishFailed",
                description: $"Failed to publish message to '{destination}': {ex.Message}"));
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="destination"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentNullException"><paramref name="messageHandler"/> or <paramref name="options"/> is <see langword="null"/></exception>
    /// <exception cref="ObjectDisposedException">The transport has been disposed</exception>
    public async ValueTask<Result> SubscribeAsync(
        string destination,
        Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>> messageHandler,
        TransportSubscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(messageHandler);
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            await _initLock.WaitAsync(cancellationToken);
            IConnection conn;
            try
            {
                _connection ??= await _connectionFactory.CreateConnectionAsync(cancellationToken);
                conn = _connection;
            }
            finally
            {
                _initLock.Release();
            }

            var consumerChannel = await conn.CreateChannelAsync(cancellationToken: cancellationToken);

            if (options.PrefetchCount > 0)
            {
                await consumerChannel.BasicQosAsync(
                    prefetchSize: 0,
                    prefetchCount: (ushort)options.PrefetchCount,
                    global: false,
                    cancellationToken: cancellationToken);
            }

            var consumer = new AsyncEventingBasicConsumer(consumerChannel);
            consumer.ReceivedAsync += async (sender, ea) =>
            {
                var headers = new Dictionary<string, string>();
                if (ea.BasicProperties.Headers is not null)
                {
                    foreach (var pair in ea.BasicProperties.Headers)
                    {
                        if (pair.Value is byte[] bytes)
                        {
                            headers[pair.Key] = System.Text.Encoding.UTF8.GetString(bytes);
                        }
                        else if (pair.Value is not null)
                        {
                            headers[pair.Key] = pair.Value.ToString() ?? string.Empty;
                        }
                    }
                }

                headers.TryGetValue("traceparent", out var traceParent);
                headers.TryGetValue("causation-id", out var causationId);
                headers.TryGetValue("tenant-id", out var tenantId);
                headers.TryGetValue("partition-key", out var partitionKey);
                var schemaVersion = headers.TryGetValue("schema-version", out var sVer) && int.TryParse(sVer, out var parsedVer) ? parsedVer : 1;

                var meta = new TransportMessageMetadata(
                    MessageId: ea.BasicProperties.MessageId ?? Guid.NewGuid().ToString("N"),
                    MessageType: ea.BasicProperties.Type ?? "application/octet-stream",
                    Timestamp: DateTimeOffset.UtcNow,
                    CorrelationId: ea.BasicProperties.CorrelationId ?? Guid.NewGuid().ToString("N"),
                    CausationId: causationId,
                    TraceParent: traceParent,
                    TenantId: tenantId,
                    PartitionKey: partitionKey,
                    ContentType: ea.BasicProperties.ContentType ?? "application/json",
                    SchemaVersion: schemaVersion,
                    Headers: headers);

                var ackResult = await messageHandler(ea.Body, meta, cancellationToken);

                switch (ackResult)
                {
                    case TransportAckResult.Ack:
                        await consumerChannel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken);
                        break;
                    case TransportAckResult.NackRequeue:
                        await consumerChannel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken);
                        break;
                    case TransportAckResult.DeadLetter:
                    default:
                        await consumerChannel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken);
                        break;
                }
            };

            await consumerChannel.BasicConsumeAsync(
                queue: destination,
                autoAck: false,
                consumerTag: string.Empty,
                noLocal: false,
                exclusive: false,
                arguments: null,
                consumer: consumer,
                cancellationToken: cancellationToken);

            return Result.Success();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to subscribe to RabbitMQ queue '{Destination}'", destination);
            return Result.Failure(Error.Failure(
                code: "RabbitMQ.SubscribeFailed",
                description: $"Failed to subscribe to queue '{destination}': {ex.Message}"));
        }
    }

    /// <summary>
    /// Asynchronously releases the resources used by this instance.
    /// </summary>
    /// <returns>A value task representing the asynchronous disposal operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_publishChannel is not null)
        {
            await _publishChannel.CloseAsync();
            _publishChannel.Dispose();
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }

        _initLock.Dispose();
        _publishLock.Dispose();
    }

    /// <summary>
    /// Releases the resources used by this instance.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _publishChannel?.Dispose();
        _connection?.Dispose();
        _initLock.Dispose();
        _publishLock.Dispose();
    }
}




