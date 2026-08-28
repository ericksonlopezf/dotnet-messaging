// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Middleware;

using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;

/// <summary>
/// Encapsulates an asynchronous continuation step in the message processing middleware pipeline.
/// </summary>
/// <param name="context">The ambient message execution context.</param>
/// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
/// <returns>
/// A value task representing the asynchronous operation. The task result contains a <see cref="Result"/>
/// indicating the execution outcome.
/// </returns>
public delegate ValueTask<Result> MessageExecutionDelegate(
    MessageContext context,
    CancellationToken cancellationToken);

/// <summary>
/// Defines an interceptor middleware component in the message consumption pipeline.
/// </summary>
public interface IMessageMiddleware
{
    /// <summary>
    /// Invokes the middleware logic and continues execution along the pipeline.
    /// </summary>
    /// <param name="context">The ambient message execution context.</param>
    /// <param name="next">The delegate representing the next step in the pipeline.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains a <see cref="Result"/>
    /// indicating the execution outcome.
    /// </returns>
    ValueTask<Result> InvokeAsync(
        MessageContext context,
        MessageExecutionDelegate next,
        CancellationToken cancellationToken);
}



