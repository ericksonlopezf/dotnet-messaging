// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Dispatch;

using System.Collections.Concurrent;
using System.Reflection;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Middleware;
using EricksonLopez.Messaging.Serialization;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Dispatches incoming raw serialized payloads and metadata to registered strongly-typed handlers through precompiled invokers and middleware.
/// </summary>
public sealed class DefaultMessageDispatcher : IMessageDispatcher, IHandlerRegistry
{
    private readonly IMessageSerializer _serializer;
    private readonly MiddlewarePipeline _pipeline;
    private readonly ConcurrentDictionary<string, List<HandlerBinding>> _bindings;
    private readonly ConcurrentDictionary<string, UpcastRoute> _upcastRoutes;
    private readonly ConcurrentDictionary<Type, string> _upcastSourceTypes;
    private readonly object _registrationLock = new();

    private sealed record UpcastRoute(Type SourceType, string TargetTypeName);

    /// <summary>
    /// Represents a compiled binding between a message type, its handler type, and an asynchronous invocation delegate.
    /// </summary>
    /// <param name="MessageType">The CLR type of the message.</param>
    /// <param name="HandlerType">The CLR type of the handler.</param>
    /// <param name="Invoker">The asynchronous invocation delegate.</param>
    public sealed record HandlerBinding(
        Type MessageType,
        Type HandlerType,
        Func<IServiceProvider, object, MessageContext, CancellationToken, ValueTask<Result>> Invoker)
    {
        internal MessageExecutionDelegate? ExecutionChain { get; init; }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultMessageDispatcher"/> class with the specified serializer, middlewares, and registrations.
    /// </summary>
    /// <param name="serializer">The message serializer used for payload deserialization.</param>
    /// <param name="middlewares">The collection of middleware components to execute during dispatch, if any.</param>
    /// <param name="bindings">The dictionary of preconfigured handler bindings, if any.</param>
    /// <param name="registrations">The collection of discovered handler registrations to configure, if any.</param>
    /// <param name="upcasters">The collection of registered message upcasters for schema version migration, if any.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serializer"/> is <see langword="null"/></exception>
    public DefaultMessageDispatcher(
        IMessageSerializer serializer,
        IEnumerable<IMessageMiddleware>? middlewares = null,
        IDictionary<string, IReadOnlyList<HandlerBinding>>? bindings = null,
        IEnumerable<IHandlerRegistration>? registrations = null,
        IEnumerable<IMessageUpcasterInvoker>? upcasters = null)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _pipeline = new MiddlewarePipeline(middlewares);
        _bindings = new ConcurrentDictionary<string, List<HandlerBinding>>(StringComparer.Ordinal);
        
        if (bindings != null)
        {
            foreach (var kvp in bindings)
            {
                var list = new List<HandlerBinding>(kvp.Value.Count);
                foreach(var b in kvp.Value)
                {
                    var copy = b;
                    if (copy.ExecutionChain == null)
                    {
                        var invoker = copy.Invoker;
                        copy = copy with { ExecutionChain = _pipeline.BuildChain((ctx, ct) => invoker(ctx.ServiceProvider, ctx.Message!, ctx, ct)) };
                    }
                    list.Add(copy);
                }
                _bindings[kvp.Key] = list;
            }
        }
        
        _upcastRoutes = new ConcurrentDictionary<string, UpcastRoute>(StringComparer.Ordinal);
        _upcastSourceTypes = new ConcurrentDictionary<Type, string>();

        if (registrations != null)
        {
            foreach (var registration in registrations)
            {
                registration.Register(this);
            }
        }

        if (upcasters != null)
        {
            foreach (var upcaster in upcasters)
            {
                RegisterUpcaster(upcaster);
            }
        }
    }

    private void RegisterUpcaster(IMessageUpcasterInvoker upcaster)
    {
        var sourceTypeName = ResolveTypeName(upcaster.SourceType);
        var targetTypeName = ResolveTypeName(upcaster.TargetType);

        _upcastRoutes[sourceTypeName] = new UpcastRoute(upcaster.SourceType, targetTypeName);
        _upcastRoutes[upcaster.SourceType.Name] = new UpcastRoute(upcaster.SourceType, targetTypeName);
        if (upcaster.SourceType.FullName is not null)
        {
            _upcastRoutes[upcaster.SourceType.FullName] = new UpcastRoute(upcaster.SourceType, targetTypeName);
        }

        _upcastSourceTypes[upcaster.SourceType] = targetTypeName;
    }

    private static string ResolveTypeName(Type type)
    {
        var attr = type.GetCustomAttribute<MessageTypeAttribute>();
        return attr?.TypeName ?? type.Name;
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

        Func<IServiceProvider, object, MessageContext, CancellationToken, ValueTask<Result>> invoker = static async (sp, msg, ctx, ct) =>
        {
            try
            {
                var handler = sp.GetRequiredService<THandler>();
                return await handler.HandleAsync((TMessage)msg, ctx, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw; // Let timeout middleware handle cancellation explicitly
            }
            catch (Exception ex)
            {
                return Result.Failure(Error.Unexpected(
                    code: "Messaging.HandlerUnhandledException",
                    description: $"Unhandled exception in handler {typeof(THandler).Name}: {ex.Message}"));
            }
        };

        var chain = _pipeline.BuildChain((ctx, ct) => invoker(ctx.ServiceProvider, ctx.Message!, ctx, ct));

        var binding = new HandlerBinding(
            MessageType: typeof(TMessage),
            HandlerType: typeof(THandler),
            Invoker: invoker)
        {
            ExecutionChain = chain
        };

        lock (_registrationLock)
        {
            var list = _bindings.GetOrAdd(typeName, _ => new List<HandlerBinding>());
            foreach (var existing in list)
            {
                if (existing.HandlerType == typeof(THandler))
                {
                    return; // Avoid duplicating the same exact handler registration
                }
            }
            list.Add(binding);
        }
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
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<HandlerBinding> effectiveBindings;
        Type deserializationType;

        if (_bindings.TryGetValue(messageType, out var directBindings))
        {
            var currentType = directBindings[0].MessageType;
            var currentBindings = directBindings;
            while (_upcastSourceTypes.TryGetValue(currentType, out var nextTypeName) &&
                   _bindings.TryGetValue(nextTypeName, out var nextBindingList))
            {
                currentBindings = nextBindingList;
                currentType = nextBindingList[0].MessageType;
            }

            effectiveBindings = currentBindings;
            deserializationType = directBindings[0].MessageType;
        }
        else if (_upcastRoutes.TryGetValue(messageType, out var upcastRoute))
        {
            var currentRoute = upcastRoute;
            List<HandlerBinding>? resolvedBindings = null;
            while (true)
            {
                if (_bindings.TryGetValue(currentRoute.TargetTypeName, out var foundBindings))
                {
                    resolvedBindings = foundBindings;
                    var currentType = foundBindings[0].MessageType;
                    while (_upcastSourceTypes.TryGetValue(currentType, out var nextTypeName) &&
                           _bindings.TryGetValue(nextTypeName, out var nextBindingList))
                    {
                        resolvedBindings = nextBindingList;
                        currentType = nextBindingList[0].MessageType;
                    }
                    break;
                }

                if (_upcastRoutes.TryGetValue(currentRoute.TargetTypeName, out var nextRoute))
                {
                    currentRoute = nextRoute;
                }
                else
                {
                    break;
                }
            }

            if (resolvedBindings is not null && resolvedBindings.Count > 0)
            {
                effectiveBindings = resolvedBindings;
                deserializationType = upcastRoute.SourceType;
            }
            else
            {
                return Result.Failure(Error.NotFound(
                    code: "Messaging.HandlerNotFound",
                    description: $"No registered message handler found for message type identifier '{messageType}'."));
            }
        }
        else
        {
            return Result.Failure(Error.NotFound(
                code: "Messaging.HandlerNotFound",
                description: $"No registered message handler found for message type identifier '{messageType}'."));
        }

        object deserializedMessage;
        try
        {
            deserializedMessage = _serializer.Deserialize(payload, deserializationType);
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

        if (effectiveBindings.Count == 1)
        {
            var binding = effectiveBindings[0];
            if (binding.ExecutionChain is not null)
            {
                return await binding.ExecutionChain(context, cancellationToken).ConfigureAwait(false);
            }
            return await _pipeline.ExecuteAsync(
                context,
                (ctx, ct) => binding.Invoker(ctx.ServiceProvider, ctx.Message ?? deserializedMessage, ctx, ct),
                cancellationToken).ConfigureAwait(false);
        }

        var errors = new List<Error>();
        foreach (var binding in effectiveBindings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Result result;
            if (binding.ExecutionChain is not null)
            {
                result = await binding.ExecutionChain(context, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                result = await _pipeline.ExecuteAsync(
                    context,
                    (ctx, ct) => binding.Invoker(ctx.ServiceProvider, ctx.Message ?? deserializedMessage, ctx, ct),
                    cancellationToken).ConfigureAwait(false);
            }

            if (result.IsFailure)
            {
                errors.Add(result.Error);
            }
        }

        if (errors.Count > 0)
        {
            return Result.Failure(Error.Failure("Messaging.DispatchFailed", $"Dispatch completed with {errors.Count} handler errors."));
        }

        return Result.Success();
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="items"/> or <paramref name="serviceProvider"/> is <see langword="null"/></exception>
    public async ValueTask<Result> DispatchBatchAsync(
        IEnumerable<MessageDispatchItem> items,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var errors = new List<Error>();

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await DispatchAsync(item.MessageType, item.Payload, item.Metadata, serviceProvider, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                errors.Add(result.Error);
            }
        }

        if (errors.Count > 0)
        {
            return Result.Failure(Error.Failure("Messaging.BatchPartialFailure", $"Batch completed with {errors.Count} errors."));
        }

        return Result.Success();
    }
}
