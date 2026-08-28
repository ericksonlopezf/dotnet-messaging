// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Contracts;

using EricksonLopez.Result;

/// <summary>
/// Defines a strongly-typed consumer handler for processing distributed messages.
/// </summary>
/// <typeparam name="TMessage">The type of message payload to handle.</typeparam>
public interface IMessageHandler<in TMessage> where TMessage : notnull
{
    /// <summary>
    /// Handles the specified message asynchronously and returns a functional <see cref="Result"/>.
    /// </summary>
    /// <param name="message">The incoming strongly-typed message instance to handle.</param>
    /// <param name="context">The ambient execution context containing metadata, scope, and items.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains a <see cref="Result"/>
    /// representing the processing outcome.
    /// </returns>
    ValueTask<Result> HandleAsync(
        TMessage message,
        MessageContext context,
        CancellationToken cancellationToken = default);
}




