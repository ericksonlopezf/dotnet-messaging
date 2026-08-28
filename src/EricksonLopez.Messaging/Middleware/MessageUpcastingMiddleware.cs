// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Middleware;

using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Defines non-generic invocation of registered message upcasters during pipeline execution.
/// </summary>
public interface IMessageUpcasterInvoker
{
    /// <summary>
    /// Gets the source message type from which this upcaster transforms.
    /// </summary>
    Type SourceType { get; }

    /// <summary>
    /// Transforms the source message instance to its upgraded schema target.
    /// </summary>
    /// <param name="message">The source message instance.</param>
    /// <param name="metadata">The message metadata.</param>
    /// <param name="serviceProvider">The service provider for dependency resolution.</param>
    /// <returns>The upgraded message instance.</returns>
    object Upcast(object message, TransportMessageMetadata metadata, IServiceProvider serviceProvider);
}

/// <summary>
/// Adapts <see cref="IMessageUpcaster{TOldMessage, TNewMessage}"/> implementations to <see cref="IMessageUpcasterInvoker"/>.
/// </summary>
/// <typeparam name="TOld">The source message type.</typeparam>
/// <typeparam name="TNew">The target upgraded message type.</typeparam>
/// <typeparam name="TUpcaster">The type of upcaster implementation.</typeparam>
public sealed class MessageUpcasterInvoker<TOld, TNew, TUpcaster> : IMessageUpcasterInvoker
    where TOld : class, IMessage
    where TNew : class, IMessage
    where TUpcaster : class, IMessageUpcaster<TOld, TNew>
{
    /// <inheritdoc />
    public Type SourceType => typeof(TOld);

    /// <inheritdoc />
    public object Upcast(object message, TransportMessageMetadata metadata, IServiceProvider serviceProvider)
    {
        var upcaster = serviceProvider.GetRequiredService<TUpcaster>();
        return upcaster.Upcast((TOld)message, metadata);
    }
}

/// <summary>
/// Provides middleware that detects legacy message schemas and applies registered upcasters to migrate payloads to the current contract version.
/// </summary>
public sealed class MessageUpcastingMiddleware : IMessageMiddleware
{
    private readonly IEnumerable<IMessageUpcasterInvoker> _upcasters;
    private readonly ILogger<MessageUpcastingMiddleware> _logger;
    private readonly ConcurrentDictionary<Type, IMessageUpcasterInvoker?> _cache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageUpcastingMiddleware"/> class with the specified upcasters and logger.
    /// </summary>
    /// <param name="upcasters">The collection of registered message upcaster invokers.</param>
    /// <param name="logger">The logger instance, if specified.</param>
    public MessageUpcastingMiddleware(
        IEnumerable<IMessageUpcasterInvoker> upcasters,
        ILogger<MessageUpcastingMiddleware>? logger = null)
    {
        _upcasters = upcasters ?? Array.Empty<IMessageUpcasterInvoker>();
        _logger = logger ?? NullLogger<MessageUpcastingMiddleware>.Instance;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="next"/> is <see langword="null"/></exception>
    public async ValueTask<Result> InvokeAsync(
        MessageContext context,
        MessageExecutionDelegate next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        cancellationToken.ThrowIfCancellationRequested();

        if (context.Message is not null)
        {
            var msgType = context.Message.GetType();
            var invoker = _cache.GetOrAdd(msgType, t =>
            {
                foreach (var u in _upcasters)
                {
                    if (u.SourceType == t)
                    {
                        return u;
                    }
                }
                return null;
            });

            if (invoker is not null)
            {
                try
                {
                    var upgradedMessage = invoker.Upcast(context.Message, context.Metadata, context.ServiceProvider);
                    _logger.LogDebug(
                        "Upcasted message '{MessageId}' from '{SourceType}' to '{TargetType}'",
                        context.Metadata.MessageId,
                        msgType.Name,
                        upgradedMessage.GetType().Name);

                    context.Message = upgradedMessage;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upcast message '{MessageId}' of type '{SourceType}'", context.Metadata.MessageId, msgType.Name);
                    return Result.Failure(Error.Validation(
                        code: "Messaging.UpcastFailed",
                        description: $"Failed to upcast message of type '{msgType.FullName}': {ex.Message}"));
                }
            }
        }

        return await next(context, cancellationToken).ConfigureAwait(false);
    }
}

