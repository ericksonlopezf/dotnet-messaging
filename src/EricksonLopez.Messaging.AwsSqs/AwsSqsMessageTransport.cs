// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.SQS;
using Amazon.SQS.Model;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EricksonLopez.Messaging.Transport.AwsSqs;

using AppMessageMetadata = EricksonLopez.Messaging.Contracts.TransportMessageMetadata;
using AppResult = EricksonLopez.Result.Result;

/// <summary>
/// Provides an AWS SQS message transport driver implementing <see cref="IMessageTransport"/>.
/// </summary>
public sealed class AwsSqsMessageTransport : IMessageTransport, IDisposable
{
    private readonly AwsSqsTransportOptions _options;
    private readonly ILogger<AwsSqsMessageTransport> _logger;
    private readonly IAmazonSQS _sqsClient;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _subscriptions = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AwsSqsMessageTransport"/> class with the specified options, SQS client, and logger.
    /// </summary>
    /// <param name="options">The AWS SQS transport configuration options, if specified.</param>
    /// <param name="sqsClient">The explicit Amazon SQS client instance, if specified.</param>
    /// <param name="logger">The logger instance, if specified.</param>
    public AwsSqsMessageTransport(
        IOptions<AwsSqsTransportOptions>? options = null,
        IAmazonSQS? sqsClient = null,
        ILogger<AwsSqsMessageTransport>? logger = null)
    {
        _options = options?.Value ?? new AwsSqsTransportOptions();
        _logger = logger ?? NullLogger<AwsSqsMessageTransport>.Instance;

        if (sqsClient is not null)
        {
            _sqsClient = sqsClient;
        }
        else
        {
            var config = new AmazonSQSConfig();
            if (!string.IsNullOrWhiteSpace(_options.ServiceUrl))
            {
                config.ServiceURL = _options.ServiceUrl;
            }
            else
            {
                config.RegionEndpoint = RegionEndpoint.GetBySystemName(_options.Region);
            }

            _sqsClient = new AmazonSQSClient(config);
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
            var messageBody = Convert.ToBase64String(payload.Span);
            var attributes = new Dictionary<string, MessageAttributeValue>();

            if (!string.IsNullOrWhiteSpace(metadata.MessageId))
            {
                attributes["message-id"] = new MessageAttributeValue { DataType = "String", StringValue = metadata.MessageId };
            }
            if (!string.IsNullOrWhiteSpace(metadata.MessageType))
            {
                attributes["message-type"] = new MessageAttributeValue { DataType = "String", StringValue = metadata.MessageType };
            }
            if (!string.IsNullOrWhiteSpace(metadata.CorrelationId))
            {
                attributes["correlation-id"] = new MessageAttributeValue { DataType = "String", StringValue = metadata.CorrelationId };
            }
            if (!string.IsNullOrWhiteSpace(metadata.TenantId))
            {
                attributes["tenant-id"] = new MessageAttributeValue { DataType = "String", StringValue = metadata.TenantId };
            }
            if (!string.IsNullOrWhiteSpace(metadata.TraceParent))
            {
                attributes["traceparent"] = new MessageAttributeValue { DataType = "String", StringValue = metadata.TraceParent };
            }
            if (!string.IsNullOrWhiteSpace(metadata.CausationId))
            {
                attributes["causation-id"] = new MessageAttributeValue { DataType = "String", StringValue = metadata.CausationId };
            }
            if (!string.IsNullOrWhiteSpace(metadata.ContentType))
            {
                attributes["content-type"] = new MessageAttributeValue { DataType = "String", StringValue = metadata.ContentType };
            }
            if (metadata.SchemaVersion > 1)
            {
                attributes["schema-version"] = new MessageAttributeValue { DataType = "String", StringValue = metadata.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            }
            if (metadata.Headers is not null)
            {
                foreach (var (key, value) in metadata.Headers)
                {
                    attributes[key] = new MessageAttributeValue { DataType = "String", StringValue = value };
                }
            }

            var request = new SendMessageRequest
            {
                QueueUrl = destination,
                MessageBody = messageBody,
                MessageAttributes = attributes
            };

            if (!string.IsNullOrWhiteSpace(metadata.PartitionKey))
            {
                request.MessageGroupId = metadata.PartitionKey;
                request.MessageDeduplicationId = !string.IsNullOrWhiteSpace(metadata.MessageId) ? metadata.MessageId : Guid.NewGuid().ToString("N");
            }

            await _sqsClient.SendMessageAsync(request, cancellationToken);
            return AppResult.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to AWS SQS queue {QueueUrl}", destination);
            return AppResult.Failure(EricksonLopez.Result.Error.Failure("AwsSqs.PublishFailed", ex.Message));
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

        _ = Task.Run(async () =>
        {
            var receiveRequest = new ReceiveMessageRequest
            {
                QueueUrl = destination,
                WaitTimeSeconds = _options.WaitTimeSeconds,
                MaxNumberOfMessages = Math.Min(10, _options.MaxNumberOfMessages),
                MessageAttributeNames = ["All"]
            };

            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var response = await _sqsClient.ReceiveMessageAsync(receiveRequest, cts.Token);
                    if (response?.Messages is not null)
                    {
                        foreach (var msg in response.Messages)
                        {
                            byte[] payloadBytes;
                            try
                            {
                                payloadBytes = Convert.FromBase64String(msg.Body);
                            }
                            catch
                            {
                                payloadBytes = Encoding.UTF8.GetBytes(msg.Body);
                            }

                            var headersDict = new Dictionary<string, string>();
                            if (msg.MessageAttributes is not null)
                            {
                                foreach (var attr in msg.MessageAttributes)
                                {
                                    headersDict[attr.Key] = attr.Value.StringValue;
                                }
                            }

                            var metadata = new AppMessageMetadata(
                                MessageId: headersDict.GetValueOrDefault("message-id") ?? msg.MessageId ?? Guid.NewGuid().ToString("N"),
                                MessageType: headersDict.GetValueOrDefault("message-type") ?? "AwsSqsMessage",
                                Timestamp: DateTimeOffset.UtcNow,
                                CorrelationId: headersDict.GetValueOrDefault("correlation-id") ?? Guid.NewGuid().ToString("N"),
                                CausationId: headersDict.GetValueOrDefault("causation-id"),
                                TraceParent: headersDict.GetValueOrDefault("traceparent"),
                                TenantId: headersDict.GetValueOrDefault("tenant-id"),
                                PartitionKey: headersDict.GetValueOrDefault("partition-key"),
                                ContentType: headersDict.GetValueOrDefault("content-type") ?? "application/json",
                                SchemaVersion: int.TryParse(headersDict.GetValueOrDefault("schema-version"), System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 1,
                                Headers: headersDict);

                            var ackResult = await messageHandler(payloadBytes, metadata, cts.Token);
                            if (ackResult == TransportAckResult.Ack)
                            {
                                await _sqsClient.DeleteMessageAsync(destination, msg.ReceiptHandle, cts.Token);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error polling AWS SQS queue {QueueUrl}", destination);
                    await Task.Delay(1000, cts.Token);
                }
            }
        }, cts.Token);

        return ValueTask.FromResult(AppResult.Success());
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var cts in _subscriptions.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _subscriptions.Clear();

        _sqsClient.Dispose();
    }
}

