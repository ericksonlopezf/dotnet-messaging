// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Middleware;

using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;

/// <summary>
/// Provides safety middleware that catches unhandled exceptions during handler execution and converts them into functional <see cref="Result"/> failures.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OperationCanceledException"/> thrown when the pipeline's <see cref="CancellationToken"/> is already
/// cancelled is caught and returned as <c>Result.Failure(Error.Failure("Messaging.Cancelled", ...))</c>.
/// This prevents cancellation from appearing as an unexpected error in consumer error counters.
/// </para>
/// <para>
/// All other exceptions are caught and returned as <c>Result.Failure(Error.Unexpected("Messaging.UnhandledException", ...))</c>.
/// The exception message is included in the error description. No stack trace is captured in the Result;
/// the exception is available via logs produced by <see cref="LoggingMiddleware"/> if it is also in the pipeline.
/// </para>
/// </remarks>
public sealed class ExceptionHandlingMiddleware : IMessageMiddleware
{
    /// <inheritdoc />
    public async ValueTask<Result> InvokeAsync(
        MessageContext context,
        MessageExecutionDelegate next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(Error.Failure(
                code: "Messaging.Cancelled",
                description: "Message processing was cancelled by caller or host shutdown."));
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Unexpected(
                code: "Messaging.UnhandledException",
                description: $"Unhandled exception of type '{ex.GetType().Name}': {ex.Message}"));
        }
    }
}



