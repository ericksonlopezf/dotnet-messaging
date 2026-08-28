// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Dispatch;

using System.Collections.Concurrent;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Middleware;
using EricksonLopez.Messaging.Serialization;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Dispatches incoming raw serialized payloads and metadata to registered strongly-typed handlers through precompiled invokers and middleware.
/// </summary>
public sealed class DefaultMessageDispatcher : IMessageDispatcher
{
    private readonly IMessageSerializer _serializer;
    private readonly MiddlewarePipeline _pipeline;
    private readonly ConcurrentDictionary<string, HandlerBinding> _bindings;

    /// <summary>
    /// Represents an internal binding between a message type, its handler type, and an asynchronous invocation delegate.
    /// </summary>
    /// <param name="MessageType">The CLR type of the message.</param>
    /// <param name="HandlerType">The CLR type of the handler.</param>
    /// <param name="Invoker">The asynchronous invocation delegate.</param>
    public sealed record HandlerBinding(
        Type MessageType,
        Type HandlerType,
        Func<IServiceProvider, object, MessageContext, CancellationToken, ValueTask<Result>> Invoker);

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultMessageDispatcher"/> class with the specified serializer, middlewares, and registrations.
    /// </summary>
    /// <param name="serializer">The message serializer used for payload deserialization.</param>
    /// <param name="middlewares">The collection of middleware components to execute during dispatch, if any.</param>
    /// <param name="bindings">The dictionary of preconfigured handler bindings, if any.</param>
    /// <param name="registrations">The collection of discovered handler registrations to configure, if any.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serializer"/> is <see langword="null"/></exception>
    public DefaultMessageDispatcher(
        IMessageSerializer serializer,
        IEnumerable<IMessageMiddleware>? middlewares = null,
        IDictionary<string, HandlerBinding>? bindings = null,
        IEnumerable<IHandlerRegistration>? registrations = null)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _pipeline = new MiddlewarePipeline(middlewares);
        _bindings = bindings != null
            ? new ConcurrentDictionary<string, HandlerBinding>(bindings, StringComparer.Ordinal)
            : new ConcurrentDictionary<string, HandlerBinding>(StringComparer.Ordinal);

        if (registrations != null)
        {
            foreach (var registration in registrations)
            {
                registration.Register(this);
            }
        }
    }

    /// <summary>
    /// Registers a handler binding for the specified message type identifier.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message payload to handle.</typeparam>
    /// <typeparam name="THandler">The type of the message handler.</typeparam>
    /// <param name="typeName">The unique message type identifier to associate with the handler.</param>
    /// <exception cref="ArgumentException"><paramref name="typeName"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public void RegisterHandler<
        TMessage,
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(string typeName)
        where TMessage : notnull
        where THandler : notnull, IMessageHandler<TMessage>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);

        var binding = new HandlerBinding(
            MessageType: typeof(TMessage),
            HandlerType: typeof(THandler),
            Invoker: static async (sp, msg, ctx, ct) =>
            {
                var handler = sp.GetRequiredService<THandler>();
                return await handler.HandleAsync((TMessage)msg, ctx, ct).ConfigureAwait(false);
            });

        _bindings[typeName] = binding;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="messageType"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentNullException"><paramref name="metadata"/> or <paramref name="serviceProvider"/> is <see langword="null"/></exception>
    public async ValueTask<Result> DispatchAsync(
        string messageType,
        ReadOnlyMemory<byte> payload,
        TransportMessageMetadata metadata,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        if (!_bindings.TryGetValue(messageType, out var binding))
        {
            return Result.Failure(Error.NotFound(
                code: "Messaging.HandlerNotFound",
                description: $"No registered message handler found for message type identifier '{messageType}'."));
        }

        object deserializedMessage;
        try
        {
            deserializedMessage = _serializer.Deserialize(payload, binding.MessageType);
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Validation(
                code: "Messaging.DeserializationFailed",
                description: $"Failed to deserialize payload for type '{messageType}': {ex.Message}"));
        }

        var context = new MessageContext(metadata, serviceProvider, cancellationToken)
        {
            Message = deserializedMessage
        };

        return await _pipeline.ExecuteAsync(
            context,
            (ctx, ct) => binding.Invoker(ctx.ServiceProvider, deserializedMessage, ctx, ct),
            cancellationToken).ConfigureAwait(false);
    }
}




