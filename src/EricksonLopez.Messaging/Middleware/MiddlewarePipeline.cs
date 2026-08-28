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
    /// Executes the middleware pipeline terminating at the specified terminal handler delegate.
    /// </summary>
    /// <param name="context">The ambient message execution context.</param>
    /// <param name="terminalHandler">The terminal handler delegate to execute at the end of the pipeline.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains a <see cref="Result"/>
    /// indicating the execution outcome.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="terminalHandler"/> is <see langword="null"/></exception>
    public ValueTask<Result> ExecuteAsync(
        MessageContext context,
        MessageExecutionDelegate terminalHandler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(terminalHandler);

        var runner = new PipelineRunner(_middlewares, terminalHandler);
        return runner.InvokeNextAsync(context, cancellationToken);
    }

    private sealed class PipelineRunner
    {
        private readonly IMessageMiddleware[] _middlewares;
        private readonly MessageExecutionDelegate _terminalHandler;
        private int _index;

        public PipelineRunner(IMessageMiddleware[] middlewares, MessageExecutionDelegate terminalHandler)
        {
            _middlewares = middlewares;
            _terminalHandler = terminalHandler;
        }

        public ValueTask<Result> InvokeNextAsync(MessageContext context, CancellationToken cancellationToken)
        {
            if (_index >= _middlewares.Length)
            {
                return _terminalHandler(context, cancellationToken);
            }

            var middleware = _middlewares[_index++];
            return middleware.InvokeAsync(context, InvokeNextAsync, cancellationToken);
        }
    }
}




