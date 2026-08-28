// Copyright © Erickson Lopez. MIT License.
namespace EricksonLopez.Messaging.Middleware;

using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

/// <summary>
/// Provides timeout middleware enforcing maximum execution limits on message handler invocations via linked cancellation tokens.
/// </summary>
public sealed class HandlerTimeoutMiddleware : IMessageMiddleware
{
    private readonly HandlerTimeoutOptions _options;
    private readonly ILogger<HandlerTimeoutMiddleware> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="HandlerTimeoutMiddleware"/> class with the specified options and logger.
    /// </summary>
    /// <param name="options">The timeout configuration options.</param>
    /// <param name="logger">The logger instance, if specified.</param>
    public HandlerTimeoutMiddleware(
        IOptions<HandlerTimeoutOptions>? options = null,
        ILogger<HandlerTimeoutMiddleware>? logger = null)
    {
        _options = options?.Value ?? new HandlerTimeoutOptions();
        _logger = logger ?? NullLogger<HandlerTimeoutMiddleware>.Instance;
        _timeProvider = _options.TimeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HandlerTimeoutMiddleware"/> class with explicit options.
    /// </summary>
    /// <param name="options">The timeout configuration options.</param>
    /// <param name="logger">The logger instance, if specified.</param>
    public HandlerTimeoutMiddleware(
        HandlerTimeoutOptions options,
        ILogger<HandlerTimeoutMiddleware>? logger = null)
        : this(Options.Create(options), logger)
    {
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

        if (_options.Timeout <= TimeSpan.Zero || _options.Timeout == Timeout.InfiniteTimeSpan)
        {
            return await next(context, cancellationToken).ConfigureAwait(false);
        }

        using var timeoutCts = new CancellationTokenSource(_options.Timeout, _timeProvider);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            return await next(context, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Message processing timed out after {TimeoutMs}ms for message '{MessageId}'", _options.Timeout.TotalMilliseconds, context.Metadata.MessageId);
            return Result.Failure(Error.Failure(
                code: "Messaging.Handler.Timeout",
                description: $"Message processing exceeded the configured timeout of {_options.Timeout.TotalSeconds}s."));
        }
    }
}
