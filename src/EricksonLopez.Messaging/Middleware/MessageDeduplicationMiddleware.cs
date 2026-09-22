// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Middleware;

using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Diagnostics;
using Result = EricksonLopez.Result.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

/// <summary>
/// Intercepts incoming messages to detect and suppress duplicate executions based on message identity.
/// </summary>
public sealed class MessageDeduplicationMiddleware : IMessageMiddleware
{
    private readonly IMessageDeduplicationStore _store;
    private readonly MessageDeduplicationOptions _options;
    private readonly ILogger<MessageDeduplicationMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageDeduplicationMiddleware"/> class.
    /// </summary>
    /// <param name="store">The deduplication state store.</param>
    /// <param name="options">The deduplication options.</param>
    /// <param name="logger">The diagnostic logger.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/></exception>
    public MessageDeduplicationMiddleware(
        IMessageDeduplicationStore store,
        IOptions<MessageDeduplicationOptions>? options = null,
        ILogger<MessageDeduplicationMiddleware>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options?.Value ?? new MessageDeduplicationOptions();
        _logger = logger ?? NullLogger<MessageDeduplicationMiddleware>.Instance;
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

        if (!_options.Enabled)
        {
            return await next(context, cancellationToken).ConfigureAwait(false);
        }

        var messageId = context.Metadata.MessageId;
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return await next(context, cancellationToken).ConfigureAwait(false);
        }

        var isNew = await _store.TryAcquireAsync(messageId, _options.Expiration, cancellationToken).ConfigureAwait(false);
        if (!isNew)
        {
            MessagingDiagnostics.MessagesDeduplicated.Add(1);
            _logger.LogInformation(
                "Duplicate message '{MessageId}' of type '{MessageType}' detected. Suppressing duplicate execution.",
                messageId,
                context.Metadata.MessageType);

            return Result.Success();
        }

        Result result;
        try
        {
            result = await next(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // If the pipeline throws an unhandled exception before reaching the dispatcher's try/catch,
            // or if a middleware throws, we must release the lock.
            await _store.ReleaseAsync(messageId, cancellationToken).ConfigureAwait(false);
            throw;
        }

        if (result.IsFailure)
        {
            // Transient failure or validation failure. Release the lock so it can be retried.
            await _store.ReleaseAsync(messageId, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}
