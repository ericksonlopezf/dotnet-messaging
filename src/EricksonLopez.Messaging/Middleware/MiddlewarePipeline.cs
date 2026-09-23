// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Middleware;

using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;

/// <summary>
/// Encapsulates a pipeline executor chaining registered middlewares and the terminal handler invocation.
/// </summary>
public sealed class MiddlewarePipeline
{
    private readonly IMessageMiddleware[] _middlewares;

    /// <summary>
    /// Initializes a new instance of the <see cref="MiddlewarePipeline"/> class with the specified middlewares.
    /// </summary>
    /// <param name="middlewares">The collection of middleware components to include in the pipeline.</param>
    public MiddlewarePipeline(IEnumerable<IMessageMiddleware>? middlewares = null)
    {
        _middlewares = middlewares switch
        {
            null => Array.Empty<IMessageMiddleware>(),
            IMessageMiddleware[] arr => arr,
            _ => middlewares.ToArray()
        };
    }

    /// <summary>
    /// Composes the middleware chain ending with the specified terminal handler.
    /// This builds and returns a pre-chained <see cref="MessageExecutionDelegate"/> that can be cached and executed
    /// repeatedly on hot dispatch paths with zero delegate or closure allocations.
    /// </summary>
    /// <param name="terminalHandler">The terminal handler delegate to execute at the end of the pipeline.</param>
    /// <returns>The composed delegate pipeline ready for zero-allocation execution.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="terminalHandler"/> is <see langword="null"/></exception>
    public MessageExecutionDelegate BuildChain(MessageExecutionDelegate terminalHandler)
    {
        ArgumentNullException.ThrowIfNull(terminalHandler);

        var current = terminalHandler;
        for (var i = _middlewares.Length - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var next = current;
            current = (ctx, ct) => middleware.InvokeAsync(ctx, next, ct);
        }

        return current;
    }

    /// <summary>
    /// Executes the middleware pipeline terminating at the specified terminal handler delegate.
    /// </summary>
    /// <param name="context">The ambient message execution context.</param>
    /// <param name="terminalHandler">The terminal handler delegate to execute at the end of the pipeline.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains a <see cref="Result"/>
    /// indicating the execution outcome.
    /// </returns>
    /// <remarks>
    /// For pipelines with 0, 1, 2, or 3 registered middlewares, the chain is executed using an inlined,
    /// non-recursive delegate composition that avoids recursive closure allocations on the hot dispatch path.
    /// For pipelines with 4 or more middlewares, execution falls back to <see cref="InvokeStepAsync"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="terminalHandler"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is cancelled before pipeline execution begins.</exception>
    public ValueTask<Result> ExecuteAsync(
        MessageContext context,
        MessageExecutionDelegate terminalHandler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(terminalHandler);
        cancellationToken.ThrowIfCancellationRequested();

        return _middlewares.Length switch
        {
            0 => terminalHandler(context, cancellationToken),
            1 => _middlewares[0].InvokeAsync(context, terminalHandler, cancellationToken),
            2 => _middlewares[0].InvokeAsync(
                context,
                (ctx, ct) => _middlewares[1].InvokeAsync(ctx, terminalHandler, ct),
                cancellationToken),
            3 => _middlewares[0].InvokeAsync(
                context,
                (ctx, ct) => _middlewares[1].InvokeAsync(
                    ctx,
                    (c, t) => _middlewares[2].InvokeAsync(c, terminalHandler, t),
                    ct),
                cancellationToken),
            _ => InvokeStepAsync(0, context, terminalHandler, cancellationToken)
        };
    }

    private ValueTask<Result> InvokeStepAsync(
        int index,
        MessageContext context,
        MessageExecutionDelegate terminalHandler,
        CancellationToken cancellationToken)
    {
        if (index >= _middlewares.Length)
        {
            return terminalHandler(context, cancellationToken);
        }

        var middleware = _middlewares[index];
        return middleware.InvokeAsync(
            context,
            (ctx, ct) => InvokeStepAsync(index + 1, ctx, terminalHandler, ct),
            cancellationToken);
    }
}




