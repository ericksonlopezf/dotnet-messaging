// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Transport.AzureServiceBus;

using System.Collections.Concurrent;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Result;
using global::Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

/// <summary>
/// Provides an Azure Service Bus message transport driver supporting batching, deferral, and subscription management.
/// </summary>
public sealed class AzureServiceBusMessageTransport : IDeferableMessageTransport, IBatchMessageTransport, IAsyncDisposable, IDisposable
{
    private readonly AzureServiceBusTransportOptions _options;
    private readonly ILogger<AzureServiceBusMessageTransport> _logger;
    private readonly ServiceBusClient _client;
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ServiceBusProcessor> _processors = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureServiceBusMessageTransport"/> class with the specified options, client, and logger.
    /// </summary>
    /// <param name="options">The Azure Service Bus transport configuration options, if specified.</param>
    /// <param name="client">The explicit Azure Service Bus client instance, if specified.</param>
    /// <param name="logger">The logger instance, if specified.</param>
    /// <exception cref="InvalidOperationException">Neither connection string, fully qualified namespace, nor client was configured</exception>
    public AzureServiceBusMessageTransport(
        IOptions<AzureServiceBusTransportOptions>? options = null,
        ServiceBusClient? client = null,
        ILogger<AzureServiceBusMessageTransport>? logger = null)
    {
        _options = options?.Value ?? new AzureServiceBusTransportOptions();
        _logger = logger ?? NullLogger<AzureServiceBusMessageTransport>.Instance;

        if (client is not null)
        {
            _client = client;
        }
        else if (!string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            _client = new ServiceBusClient(_options.ConnectionString);
        }
        else if (!string.IsNullOrWhiteSpace(_options.FullyQualifiedNamespace))
        {
            var credential = _options.Credential ?? new global::Azure.Identity.DefaultAzureCredential();
            _client = new ServiceBusClient(_options.FullyQualifiedNamespace, credential);
        }
        else
        {
            throw new InvalidOperationException("AzureServiceBus connection string, fully-qualified namespace, or explicit client must be provided.");
        }
    }

    private ServiceBusSender GetOrCreateSender(string destination) =>
        _senders.GetOrAdd(destination, name => _client.CreateSender(name));

    private static ServiceBusMessage CreateServiceBusMessage(ReadOnlyMemory<byte> payload, TransportMessageMetadata metadata)
    {
        var sbMessage = new ServiceBusMessage(new BinaryData(payload))
        {
            MessageId = metadata.MessageId,
            Subject = metadata.MessageType,
            CorrelationId = metadata.CorrelationId,
            ContentType = metadata.ContentType ?? "application/json"
        };

        if (!string.IsNullOrWhiteSpace(metadata.TraceParent))
        {
            sbMessage.ApplicationProperties["traceparent"] = metadata.TraceParent;
        }
        if (!string.IsNullOrWhiteSpace(metadata.CausationId))
        {
            sbMessage.ApplicationProperties["causation-id"] = metadata.CausationId;
        }
        if (!string.IsNullOrWhiteSpace(metadata.TenantId))
        {
            sbMessage.ApplicationProperties["tenant-id"] = metadata.TenantId;
        }
        if (!string.IsNullOrWhiteSpace(metadata.PartitionKey))
        {
            sbMessage.PartitionKey = metadata.PartitionKey;
        }
        if (metadata.SchemaVersion > 1)
        {
            sbMessage.ApplicationProperties["schema-version"] = metadata.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (metadata.Headers is not null)
        {
            foreach (var pair in metadata.Headers)
            {
                sbMessage.ApplicationProperties[pair.Key] = pair.Value;
            }
        }

        return sbMessage;
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
            var sender = GetOrCreateSender(destination);
            var sbMessage = CreateServiceBusMessage(payload, metadata);

            await sender.SendMessageAsync(sbMessage, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to publish message to Azure Service Bus destination '{Destination}'", destination);
            return Result.Failure(Error.Failure(
                code: "AzureServiceBus.PublishFailed",
                description: $"Failed to publish message to '{destination}': {ex.Message}"));
        }
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

        try
        {
            var sender = GetOrCreateSender(destination);
            var sbMessages = new List<ServiceBusMessage>(batch.Count);
            foreach (var (payload, metadata) in batch)
            {
                sbMessages.Add(CreateServiceBusMessage(payload, metadata));
            }

            await sender.SendMessagesAsync(sbMessages, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to publish batch of {Count} messages to Azure Service Bus destination '{Destination}'", batch.Count, destination);
            return Result.Failure(Error.Failure(
                code: "AzureServiceBus.BatchPublishFailed",
                description: $"Failed to publish batch to '{destination}': {ex.Message}"));
        }
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

        try
        {
            var sender = GetOrCreateSender(destination);
            var sbMessage = CreateServiceBusMessage(payload, metadata);
            sbMessage.ScheduledEnqueueTime = DateTimeOffset.UtcNow.Add(delay);

            await sender.SendMessageAsync(sbMessage, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to defer message to Azure Service Bus destination '{Destination}'", destination);
            return Result.Failure(Error.Failure(
                code: "AzureServiceBus.DeferFailed",
                description: $"Failed to defer message to '{destination}': {ex.Message}"));
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
            var processorOptions = new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = Math.Max(1, options.MaxConcurrency),
                PrefetchCount = Math.Max(0, options.PrefetchCount),
                AutoCompleteMessages = false
            };

            var processor = _client.CreateProcessor(destination, processorOptions);

            processor.ProcessMessageAsync += async args =>
            {
                var headers = new Dictionary<string, string>();
                if (args.Message.ApplicationProperties is not null)
                {
                    foreach (var pair in args.Message.ApplicationProperties)
                    {
                        headers[pair.Key] = pair.Value?.ToString() ?? string.Empty;
                    }
                }

                headers.TryGetValue("traceparent", out var traceParent);
                headers.TryGetValue("causation-id", out var causationId);
                headers.TryGetValue("tenant-id", out var tenantId);
                var partitionKey = args.Message.PartitionKey ?? (headers.TryGetValue("partition-key", out var pk) ? pk : null);
                var schemaVersion = headers.TryGetValue("schema-version", out var sVer) && int.TryParse(sVer, out var parsedVer) ? parsedVer : 1;

                var meta = new TransportMessageMetadata(
                    MessageId: args.Message.MessageId ?? Guid.NewGuid().ToString("N"),
                    MessageType: args.Message.Subject ?? "application/octet-stream",
                    Timestamp: args.Message.EnqueuedTime,
                    CorrelationId: args.Message.CorrelationId ?? Guid.NewGuid().ToString("N"),
                    CausationId: causationId,
                    TraceParent: traceParent,
                    TenantId: tenantId,
                    PartitionKey: partitionKey,
                    ContentType: args.Message.ContentType ?? "application/json",
                    SchemaVersion: schemaVersion,
                    Headers: headers);

                var ackResult = await messageHandler(args.Message.Body.ToMemory(), meta, args.CancellationToken);

                switch (ackResult)
                {
                    case TransportAckResult.Ack:
                        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                        break;
                    case TransportAckResult.NackRequeue:
                        await args.AbandonMessageAsync(args.Message, null, args.CancellationToken);
                        break;
                    default:
                        await args.DeadLetterMessageAsync(args.Message, "ProcessingError", "Message processing rejected by handler.", args.CancellationToken);
                        break;
                }
            };

            processor.ProcessErrorAsync += args =>
            {
                _logger.LogError(args.Exception, "Azure Service Bus processor error for destination '{Destination}': {ErrorSource}", destination, args.ErrorSource);
                return Task.CompletedTask;
            };

            await processor.StartProcessingAsync(cancellationToken);
            _processors[destination] = processor;

            return Result.Success();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to subscribe to Azure Service Bus destination '{Destination}'", destination);
            return Result.Failure(Error.Failure(
                code: "AzureServiceBus.SubscribeFailed",
                description: $"Failed to subscribe to '{destination}': {ex.Message}"));
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

        foreach (var processor in _processors.Values)
        {
            await processor.StopProcessingAsync();
            await processor.DisposeAsync();
        }
        _processors.Clear();

        foreach (var sender in _senders.Values)
        {
            await sender.DisposeAsync();
        }
        _senders.Clear();

        await _client.DisposeAsync();
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
        foreach (var processor in _processors.Values)
        {
            _ = processor.DisposeAsync();
        }
        _processors.Clear();

        foreach (var sender in _senders.Values)
        {
            _ = sender.DisposeAsync();
        }
        _senders.Clear();

        _ = _client.DisposeAsync();
    }
}

