// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EricksonLopez.Messaging.Extensions;

using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Dispatch;
using EricksonLopez.Messaging.Middleware;

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
    /// <para>
    /// This method is idempotent: calling it multiple times or in combination with the default pipeline
    /// will not register duplicate middleware instances. Only a single <see cref="ExceptionHandlingMiddleware"/>
    /// will be present in the resolved pipeline.
    /// </para>
    /// </remarks>
    public MessagingOptionsBuilder AddExceptionHandling()
    {
        if (!Services.Any(d => d.ServiceType == typeof(IMessageMiddleware) && d.ImplementationType == typeof(ExceptionHandlingMiddleware)))
        {
            Services.AddSingleton<IMessageMiddleware, ExceptionHandlingMiddleware>();
        }
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
    /// <para>
    /// This method is idempotent: calling it multiple times or in combination with the default pipeline
    /// will not register duplicate middleware instances.
    /// </para>
    /// </remarks>
    public MessagingOptionsBuilder AddLogging()
    {
        if (!Services.Any(d => d.ServiceType == typeof(IMessageMiddleware) && d.ImplementationType == typeof(LoggingMiddleware)))
        {
            Services.AddSingleton<IMessageMiddleware, LoggingMiddleware>();
        }
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
    /// <para>
    /// This method is idempotent: calling it multiple times or in combination with the default pipeline
    /// will not register duplicate middleware instances.
    /// </para>
    /// </remarks>
    public MessagingOptionsBuilder AddTracing()
    {
        if (!Services.Any(d => d.ServiceType == typeof(IMessageMiddleware) && d.ImplementationType == typeof(TracingMiddleware)))
        {
            Services.AddSingleton<IMessageMiddleware, TracingMiddleware>();
        }
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

    /// <summary>
    /// Adds message deduplication middleware to prevent repeated execution of duplicate messages based on message identity.
    /// </summary>
    /// <param name="configure">The delegate to configure deduplication options including retention duration, if specified.</param>
    /// <returns>The current <see cref="MessagingOptionsBuilder"/> instance.</returns>
    public MessagingOptionsBuilder AddDeduplication(Action<MessageDeduplicationOptions>? configure = null)
    {
        if (configure is not null)
        {
            Services.Configure(configure);
        }
        Services.TryAddSingleton<IMessageDeduplicationStore, InMemoryMessageDeduplicationStore>();
        Services.AddSingleton<IMessageMiddleware, MessageDeduplicationMiddleware>();
        return this;
    }

    /// <summary>
    /// Configures message consumer execution options such as unhandled failure acknowledgement behavior.
    /// </summary>
    /// <param name="configure">The delegate to configure <see cref="MessageConsumerOptions"/>.</param>
    /// <returns>The current <see cref="MessagingOptionsBuilder"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/></exception>
    public MessagingOptionsBuilder ConfigureConsumer(Action<MessageConsumerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure(configure);
        return this;
    }
}
