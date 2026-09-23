// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Dispatch;

using System.Diagnostics;
using System.Reflection;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Diagnostics;
using EricksonLopez.Messaging.Serialization;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Result;

/// <summary>
/// Publishes strongly-typed messages to distributed message transports with serialization, diagnostics, and metadata propagation.
/// </summary>
public sealed class MessagePublisher : IMessagePublisher
{
    private readonly IMessageTransport _transport;
    private readonly IMessageSerializer _serializer;
    private readonly IPartitionKeyResolver[] _partitionKeyResolvers;
    private const string MessagingSystemKey = "messaging.system";
    private const string MessagingSystemValue = "ericksonlopez.messaging";
    private const string MessagingDestinationNameKey = "messaging.destination.name";
    private const string MessagingOperationKey = "messaging.operation";
    private const string SerializationFailedErrorCode = "Messaging.SerializationFailed";

    /// <summary>
    /// Initializes a new instance of the <see cref="MessagePublisher"/> class with the specified transport and serializer.
    /// </summary>
    /// <param name="transport">The message transport implementation.</param>
    /// <param name="serializer">The message serializer used for encoding message payloads.</param>
    /// <param name="partitionKeyResolvers">Optional list of partition key resolvers for AOT-safe extraction.</param>
    /// <exception cref="ArgumentNullException"><paramref name="transport"/> or <paramref name="serializer"/> is <see langword="null"/></exception>
    public MessagePublisher(
        IMessageTransport transport,
        IMessageSerializer serializer,
        IEnumerable<IPartitionKeyResolver>? partitionKeyResolvers = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _partitionKeyResolvers = partitionKeyResolvers?.ToArray() ?? Array.Empty<IPartitionKeyResolver>();
    }

    /// <inheritdoc />
    public async ValueTask<Result> PublishAsync<TMessage>(
        TMessage message,
        MessagePublishOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageType = ResolveMessageType<TMessage>();
        var destination = !string.IsNullOrWhiteSpace(options?.Destination)
            ? options.Destination
            : messageType;

        using var activity = MessagingDiagnostics.ActivitySource.StartActivity(
            name: $"{destination} publish",
            kind: ActivityKind.Producer);

        var traceParent = Activity.Current?.Id;
        var partitionKey = options?.PartitionKey ?? ResolvePartitionKey(message);
        var metadata = TransportMessageMetadata.Create(
            messageType: messageType,
            correlationId: options?.CorrelationId,
            causationId: options?.CausationId,
            traceParent: traceParent,
            tenantId: options?.TenantId,
            partitionKey: partitionKey,
            headers: options?.Headers);

        if (activity is not null)
        {
            activity.SetTag(MessagingSystemKey, MessagingSystemValue);
            activity.SetTag(MessagingDestinationNameKey, destination);
            activity.SetTag(MessagingOperationKey, "publish");
            activity.SetTag("messaging.message.id", metadata.MessageId);
            activity.SetTag("messaging.message.type", metadata.MessageType);
            activity.SetTag("messaging.message.conversation_id", metadata.CorrelationId);
        }

        ReadOnlyMemory<byte> payload;
        try
        {
            payload = _serializer.Serialize(message);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Result.Failure(Error.Validation(
                code: SerializationFailedErrorCode,
                description: $"Failed to serialize message of type '{typeof(TMessage).FullName}': {ex.Message}"));
        }

        var result = await _transport.PublishRawAsync(destination, payload, metadata, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            MessagingDiagnostics.MessagesPublished.Add(1);
        }
        else
        {
            activity?.SetStatus(ActivityStatusCode.Error, result.Error.Description);
        }

        return result;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public async ValueTask<Result> SendAsync<TMessage>(
        TMessage message,
        string destination,
        MessageSendOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var messageType = ResolveMessageType<TMessage>();

        using var activity = MessagingDiagnostics.ActivitySource.StartActivity(
            name: $"{destination} send",
            kind: ActivityKind.Producer);

        var traceParent = Activity.Current?.Id;
        var partitionKey = options?.PartitionKey ?? ResolvePartitionKey(message);
        var metadata = TransportMessageMetadata.Create(
            messageType: messageType,
            correlationId: options?.CorrelationId,
            causationId: options?.CausationId,
            traceParent: traceParent,
            tenantId: options?.TenantId,
            partitionKey: partitionKey,
            headers: options?.Headers);

        if (activity is not null)
        {
            activity.SetTag(MessagingSystemKey, MessagingSystemValue);
            activity.SetTag(MessagingDestinationNameKey, destination);
            activity.SetTag(MessagingOperationKey, "send");
            activity.SetTag("messaging.message.id", metadata.MessageId);
            activity.SetTag("messaging.message.type", metadata.MessageType);
            activity.SetTag("messaging.message.conversation_id", metadata.CorrelationId);
        }

        ReadOnlyMemory<byte> payload;
        try
        {
            payload = _serializer.Serialize(message);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Result.Failure(Error.Validation(
                code: SerializationFailedErrorCode,
                description: $"Failed to serialize message of type '{typeof(TMessage).FullName}': {ex.Message}"));
        }

        var result = await _transport.PublishRawAsync(destination, payload, metadata, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            MessagingDiagnostics.MessagesPublished.Add(1);
        }
        else
        {
            activity?.SetStatus(ActivityStatusCode.Error, result.Error.Description);
        }

        return result;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="messages"/> is <see langword="null"/></exception>
    public async ValueTask<Result> PublishBatchAsync<TMessage>(
        IEnumerable<TMessage> messages,
        MessagePublishOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(messages);

        var messageType = ResolveMessageType<TMessage>();
        var destination = !string.IsNullOrWhiteSpace(options?.Destination)
            ? options.Destination
            : messageType;

        using var activity = MessagingDiagnostics.ActivitySource.StartActivity(
            name: $"{destination} publish_batch",
            kind: ActivityKind.Producer);

        var traceParent = Activity.Current?.Id;
        var batchList = new List<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)>();
        foreach (var message in messages)
        {
            if (message is null)
            {
                continue;
            }

            var partitionKey = options?.PartitionKey ?? ResolvePartitionKey(message);
            var metadata = TransportMessageMetadata.Create(
                messageType: messageType,
                correlationId: options?.CorrelationId,
                causationId: options?.CausationId,
                traceParent: traceParent,
                tenantId: options?.TenantId,
                partitionKey: partitionKey,
                headers: options?.Headers);

            ReadOnlyMemory<byte> payload;
            try
            {
                payload = _serializer.Serialize(message);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return Result.Failure(Error.Validation(
                    code: SerializationFailedErrorCode,
                    description: $"Failed to serialize batch message of type '{typeof(TMessage).FullName}': {ex.Message}"));
            }

            batchList.Add((payload, metadata));
        }

        if (batchList.Count == 0)
        {
            return Result.Success();
        }

        if (activity is not null)
        {
            activity.SetTag(MessagingSystemKey, MessagingSystemValue);
            activity.SetTag(MessagingDestinationNameKey, destination);
            activity.SetTag(MessagingOperationKey, "publish_batch");
            activity.SetTag("messaging.batch.count", batchList.Count);
        }

        Result result;
        if (_transport is IBatchMessageTransport batchTransport)
        {
            result = await batchTransport.PublishBatchRawAsync(destination, batchList, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            result = Result.Success();
            foreach (var (payload, metadata) in batchList)
            {
                var itemResult = await _transport.PublishRawAsync(destination, payload, metadata, cancellationToken).ConfigureAwait(false);
                if (itemResult.IsFailure)
                {
                    result = itemResult;
                    break;
                }
            }
        }

        if (result.IsSuccess)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            MessagingDiagnostics.MessagesPublished.Add(batchList.Count);
        }
        else
        {
            activity?.SetStatus(ActivityStatusCode.Error, result.Error.Description);
        }

        return result;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="messages"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public async ValueTask<Result> SendBatchAsync<TMessage>(
        IEnumerable<TMessage> messages,
        string destination,
        MessageSendOptions? options = null,
        CancellationToken cancellationToken = default) where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var messageType = ResolveMessageType<TMessage>();

        using var activity = MessagingDiagnostics.ActivitySource.StartActivity(
            name: $"{destination} send_batch",
            kind: ActivityKind.Producer);

        var traceParent = Activity.Current?.Id;
        var batchList = new List<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)>();
        foreach (var message in messages)
        {
            if (message is null)
            {
                continue;
            }

            var partitionKey = options?.PartitionKey ?? ResolvePartitionKey(message);
            var metadata = TransportMessageMetadata.Create(
                messageType: messageType,
                correlationId: options?.CorrelationId,
                causationId: options?.CausationId,
                traceParent: traceParent,
                tenantId: options?.TenantId,
                partitionKey: partitionKey,
                headers: options?.Headers);

            ReadOnlyMemory<byte> payload;
            try
            {
                payload = _serializer.Serialize(message);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return Result.Failure(Error.Validation(
                    code: SerializationFailedErrorCode,
                    description: $"Failed to serialize batch message of type '{typeof(TMessage).FullName}': {ex.Message}"));
            }

            batchList.Add((payload, metadata));
        }

        if (batchList.Count == 0)
        {
            return Result.Success();
        }

        if (activity is not null)
        {
            activity.SetTag(MessagingSystemKey, MessagingSystemValue);
            activity.SetTag(MessagingDestinationNameKey, destination);
            activity.SetTag(MessagingOperationKey, "send_batch");
            activity.SetTag("messaging.batch.count", batchList.Count);
        }

        Result result;
        if (_transport is IBatchMessageTransport batchTransport)
        {
            result = await batchTransport.PublishBatchRawAsync(destination, batchList, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            result = Result.Success();
            foreach (var (payload, metadata) in batchList)
            {
                var itemResult = await _transport.PublishRawAsync(destination, payload, metadata, cancellationToken).ConfigureAwait(false);
                if (itemResult.IsFailure)
                {
                    result = itemResult;
                    break;
                }
            }
        }

        if (result.IsSuccess)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            MessagingDiagnostics.MessagesPublished.Add(batchList.Count);
        }
        else
        {
            activity?.SetStatus(ActivityStatusCode.Error, result.Error.Description);
        }

        return result;
    }

    private static string ResolveMessageType<T>() => MessageTypeCache<T>.TypeName;

    private string? ResolvePartitionKey<TMessage>(TMessage message) where TMessage : notnull
    {
        foreach (var resolver in _partitionKeyResolvers)
        {
            var key = resolver.Resolve(message);
            if (key != null)
            {
                return key;
            }
        }
        
        return PartitionKeyExtractor<TMessage>.Extract(message);
    }
}

