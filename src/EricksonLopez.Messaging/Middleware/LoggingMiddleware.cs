// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Middleware;

using System.Diagnostics;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Provides structured logging middleware for the message processing pipeline.
/// </summary>
/// <remarks>
/// <para>
/// When an exception propagates from the next middleware or handler, <see cref="LoggingMiddleware"/> logs the error
/// at <c>Error</c> level and then <strong>re-throws the exception</strong>. It does not convert exceptions into
/// <see cref="EricksonLopez.Result.Result"/> failures.
/// </para>
/// <para>
/// If unhandled exceptions must be captured as functional results, ensure <see cref="ExceptionHandlingMiddleware"/>
/// is registered earlier in the pipeline (before <see cref="LoggingMiddleware"/>). Both middlewares are part of
/// the default pipeline registered by <c>AddMessaging()</c>.
/// </para>
/// </remarks>
public sealed class LoggingMiddleware : IMessageMiddleware
{
    private readonly ILogger<LoggingMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingMiddleware"/> class with the specified logger.
    /// </summary>
    /// <param name="logger">The logger instance, if specified.</param>
    public LoggingMiddleware(ILogger<LoggingMiddleware>? logger = null)
    {
        _logger = logger ?? NullLogger<LoggingMiddleware>.Instance;
    }

    /// <inheritdoc />
    public async ValueTask<Result> InvokeAsync(
        MessageContext context,
        MessageExecutionDelegate next,
        CancellationToken cancellationToken)
    {
        var metadata = context.Metadata;

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Processing message {MessageType} [Id: {MessageId}, CorrelationId: {CorrelationId}]",
                metadata.MessageType, metadata.MessageId, metadata.CorrelationId);
        }

        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            var result = await next(context, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "Successfully processed message {MessageType} [Id: {MessageId}] in {ElapsedMilliseconds:F2}ms",
                        metadata.MessageType, metadata.MessageId, Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
                }
            }
            else
            {
                _logger.LogWarning(
                    "Business failure processing message {MessageType} [Id: {MessageId}]: {ErrorCode} - {ErrorDescription}",
                    metadata.MessageType, metadata.MessageId, result.Error.Code, result.Error.Description);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception processing message {MessageType} [Id: {MessageId}] after {ElapsedMilliseconds:F2}ms",
                metadata.MessageType, metadata.MessageId, Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            throw;
        }
    }
}



