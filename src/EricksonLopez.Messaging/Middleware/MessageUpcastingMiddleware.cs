// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Middleware;

using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
            var visitedTypes = new HashSet<Type> { context.Message.GetType() };
            const int MaxUpcasts = 10;
            int upcastCount = 0;

            while (context.Message is not null && upcastCount < MaxUpcasts)
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

                if (invoker is null)
                {
                    break;
                }

                try
                {
                    var upgradedMessage = invoker.Upcast(context.Message, context.Metadata, context.ServiceProvider);
                    var upgradedType = upgradedMessage.GetType();

                    if (!visitedTypes.Add(upgradedType))
                    {
                        // Cycle detected (e.g. V1 -> V2 -> V1). Terminate chained upcasts safely.
                        break;
                    }

                    _logger.LogDebug(
                        "Upcasted message '{MessageId}' from '{SourceType}' to '{TargetType}'",
                        context.Metadata.MessageId,
                        msgType.Name,
                        upgradedType.Name);

                    context.Message = upgradedMessage;
                    upcastCount++;
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
