// Copyright © Erickson Lopez. MIT License.
using System;

namespace Microsoft.Extensions.DependencyInjection;

using System.Diagnostics.CodeAnalysis;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Dispatch;
using EricksonLopez.Messaging.Hosting;
using EricksonLopez.Messaging.Middleware;
using EricksonLopez.Messaging.Serialization;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Messaging.Transport.InMemory;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Provides extension methods for registering messaging services, handlers, and upcasters in an <see cref="IServiceCollection"/>.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers core messaging infrastructure and in-memory transport services in the service container.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">The delegate to configure messaging options and middleware, if specified.</param>
    /// <returns>The configured <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        Action<MessagingOptionsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IMessageSerializer, NativeAotJsonSerializer>();
        services.TryAddSingleton<IMessageTransport, InMemoryMessageTransport>();
        services.TryAddSingleton<DefaultMessageDispatcher>();
        services.TryAddSingleton<IMessageDispatcher>(sp => sp.GetRequiredService<DefaultMessageDispatcher>());
        services.TryAddSingleton<IMessagePublisher, MessagePublisher>();
        services.TryAddSingleton<IMessageConsumer, MessageConsumer>();
        services.AddHostedService<MessagingConsumerHostedService>();

        // Default Middlewares
        services.AddSingleton<IMessageMiddleware, TracingMiddleware>();
        services.AddSingleton<IMessageMiddleware, LoggingMiddleware>();
        services.AddSingleton<IMessageMiddleware, ExceptionHandlingMiddleware>();

        var builder = new MessagingOptionsBuilder(services);
        configure?.Invoke(builder);

        return services;
    }

    /// <summary>
    /// Registers a strongly-typed message handler and configures its binding in the dispatcher.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message payload to handle.</typeparam>
    /// <typeparam name="THandler">The type of the message handler implementation.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The configured <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddMessageHandler<
        TMessage,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        this IServiceCollection services)
        where TMessage : notnull
        where THandler : class, IMessageHandler<TMessage>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<THandler>();
        services.AddScoped<IMessageHandler<TMessage>>(sp => sp.GetRequiredService<THandler>());

        var typeName = MessageTypeCache<TMessage>.TypeName;

        // Auto-register binding in the dispatcher and consumer
        services.AddSingleton<IHandlerRegistration>(new HandlerRegistration<TMessage, THandler>(typeName));

        return services;
    }

    /// <summary>
    /// Registers a message upcaster for migrating legacy schema payloads to newer schema contracts.
    /// </summary>
    /// <typeparam name="TOldMessage">The previous message contract type.</typeparam>
    /// <typeparam name="TNewMessage">The upgraded target message contract type.</typeparam>
    /// <typeparam name="TUpcaster">The type of the upcaster implementation.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The configured <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddMessageUpcaster<
        TOldMessage,
        TNewMessage,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TUpcaster>(
        this IServiceCollection services)
        where TOldMessage : class, IMessage
        where TNewMessage : class, IMessage
        where TUpcaster : class, IMessageUpcaster<TOldMessage, TNewMessage>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<TUpcaster>();
        services.AddSingleton<IMessageUpcasterInvoker, MessageUpcasterInvoker<TOldMessage, TNewMessage, TUpcaster>>();

        return services;
    }
}

/// <summary>
/// Provides a fluent configuration builder for messaging middleware and options.
/// </summary>
public sealed class MessagingOptionsBuilder
{
    /// <summary>
    /// Gets the service collection being configured.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessagingOptionsBuilder"/> class with the specified service collection.
    /// </summary>
    /// <param name="services">The service collection instance to configure.</param>
    public MessagingOptionsBuilder(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>
    /// Registers a custom middleware into the message processing pipeline.
    /// </summary>
    /// <typeparam name="TMiddleware">The type of middleware implementation to register.</typeparam>
    /// <returns>The current <see cref="MessagingOptionsBuilder"/> instance.</returns>
    public MessagingOptionsBuilder AddMiddleware<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMiddleware>()
        where TMiddleware : class, IMessageMiddleware
    {
        Services.AddSingleton<IMessageMiddleware, TMiddleware>();
        return this;
    }

    /// <summary>
    /// Adds retry middleware with exponential backoff and full-jitter randomization to the message processing pipeline.
    /// </summary>
    /// <param name="configure">The delegate to configure retry options including maximum attempts, initial delay, and time provider, if specified.</param>
    /// <returns>The current <see cref="MessagingOptionsBuilder"/> instance.</returns>
    public MessagingOptionsBuilder AddRetry(Action<RetryOptions>? configure = null)
    {
        var options = new RetryOptions();
        configure?.Invoke(options);
        Services.AddSingleton<IMessageMiddleware>(_ => new RetryMiddleware(options));
        return this;
    }

    /// <summary>
    /// Adds exception handling middleware that catches unhandled exceptions and converts them into
    /// <see cref="EricksonLopez.Result.Result"/> failures, preventing raw exceptions from escaping the pipeline.
    /// </summary>
    /// <returns>The current <see cref="MessagingOptionsBuilder"/> instance.</returns>
    /// <remarks>
    /// <see cref="ExceptionHandlingMiddleware"/> is already registered in the default pipeline by <c>AddMessaging()</c>.
    /// Use this method when building a custom pipeline without the defaults, or when you need to control
    /// the position of exception handling relative to other middlewares.
    /// </remarks>
    public MessagingOptionsBuilder AddExceptionHandling()
    {
        Services.AddSingleton<IMessageMiddleware, ExceptionHandlingMiddleware>();
        return this;
    }

    /// <summary>
    /// Adds structured logging middleware that records message processing events with full context correlation.
    /// </summary>
    /// <returns>The current <see cref="MessagingOptionsBuilder"/> instance.</returns>
    /// <remarks>
    /// <see cref="LoggingMiddleware"/> is already registered in the default pipeline by <c>AddMessaging()</c>.
    /// Use this method when building a custom pipeline without the defaults, or when you need to control
    /// the position of logging relative to other middlewares.
    /// </remarks>
    public MessagingOptionsBuilder AddLogging()
    {
        Services.AddSingleton<IMessageMiddleware, LoggingMiddleware>();
        return this;
    }

    /// <summary>
    /// Adds distributed tracing middleware that propagates and creates W3C <c>traceparent</c> spans
    /// across messaging operations.
    /// </summary>
    /// <returns>The current <see cref="MessagingOptionsBuilder"/> instance.</returns>
    /// <remarks>
    /// <see cref="TracingMiddleware"/> is already registered in the default pipeline by <c>AddMessaging()</c>.
    /// Use this method when building a custom pipeline without the defaults, or when you need to control
    /// the position of tracing relative to other middlewares.
    /// </remarks>
    public MessagingOptionsBuilder AddTracing()
    {
        Services.AddSingleton<IMessageMiddleware, TracingMiddleware>();
        return this;
    }

    /// <summary>
    /// Adds circuit breaker resilience middleware to the message processing pipeline.
    /// </summary>
    /// <param name="configure">The delegate to configure circuit breaker options, if specified.</param>
    /// <returns>The current <see cref="MessagingOptionsBuilder"/> instance.</returns>
    public MessagingOptionsBuilder AddCircuitBreaker(Action<CircuitBreakerOptions>? configure = null)
    {
        if (configure is not null)
        {
            Services.Configure(configure);
        }
        Services.AddSingleton<IMessageMiddleware, CircuitBreakerMiddleware>();
        return this;
    }

    /// <summary>
    /// Adds handler execution timeout middleware to the message processing pipeline using an explicit timeout duration.
    /// </summary>
    /// <param name="timeout">The maximum allowed execution duration per message. Must be a positive, finite duration.</param>
    /// <returns>The current <see cref="MessagingOptionsBuilder"/> instance.</returns>
    public MessagingOptionsBuilder AddHandlerTimeout(TimeSpan timeout)
    {
        Services.Configure<HandlerTimeoutOptions>(opts => opts.Timeout = timeout);
        Services.AddSingleton<IMessageMiddleware, HandlerTimeoutMiddleware>();
        return this;
    }

    /// <summary>
    /// Adds handler execution timeout middleware to the message processing pipeline using a configuration delegate.
    /// </summary>
    /// <param name="configure">The delegate to configure <see cref="HandlerTimeoutOptions"/> with custom timeout and time provider settings, if specified.</param>
    /// <returns>The current <see cref="MessagingOptionsBuilder"/> instance.</returns>
    public MessagingOptionsBuilder AddHandlerTimeout(Action<HandlerTimeoutOptions>? configure = null)
    {
        if (configure is not null)
        {
            Services.Configure(configure);
        }
        Services.AddSingleton<IMessageMiddleware, HandlerTimeoutMiddleware>();
        return this;
    }

    /// <summary>
    /// Adds message schema upcasting middleware to automatically upgrade legacy message schemas.
    /// </summary>
    /// <returns>The current <see cref="MessagingOptionsBuilder"/> instance.</returns>
    public MessagingOptionsBuilder AddUpcasting()
    {
        Services.AddSingleton<IMessageMiddleware, MessageUpcastingMiddleware>();
        return this;
    }
}

internal sealed class HandlerRegistration<TMessage, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler> : HandlerRegistrationBase
    where TMessage : notnull
    where THandler : class, IMessageHandler<TMessage>
{
    public HandlerRegistration(string typeName) : base(typeName)
    {
    }

    public override void Register(DefaultMessageDispatcher dispatcher)
    {
        dispatcher.RegisterHandler<TMessage, THandler>(TypeName);
    }
}
