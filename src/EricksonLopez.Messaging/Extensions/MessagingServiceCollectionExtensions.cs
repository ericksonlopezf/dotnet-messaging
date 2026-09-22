// Copyright © Erickson Lopez. MIT License.
using System;
using System.Linq;

namespace Microsoft.Extensions.DependencyInjection;

using System.Diagnostics.CodeAnalysis;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Dispatch;
using EricksonLopez.Messaging.Extensions;
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

        services.TryAddSingleton<IMessageSerializer>(sp => new NativeAotJsonSerializer(
            sp.GetService<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(),
            sp.GetService<Microsoft.Extensions.Options.IOptions<System.Text.Json.JsonSerializerOptions>>()));
        services.TryAddSingleton<IMessageTransport, InMemoryMessageTransport>();
        services.TryAddSingleton<DefaultMessageDispatcher>();
        services.TryAddSingleton<IMessageDispatcher>(sp => sp.GetRequiredService<DefaultMessageDispatcher>());
        services.TryAddSingleton<IMessagePublisher, MessagePublisher>();
        services.TryAddSingleton<IMessageConsumer, MessageConsumer>();
        services.AddHostedService<MessagingConsumerHostedService>();

        var builder = new MessagingOptionsBuilder(services);
        builder.AddTracing();
        builder.AddLogging();
        builder.AddExceptionHandling();

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
