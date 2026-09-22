// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Tests.Extensions;

using System.Linq;
using AwesomeAssertions;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Dispatch;
using EricksonLopez.Messaging.Hosting;
using EricksonLopez.Messaging.Middleware;
using EricksonLopez.Messaging.Serialization;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

[Trait("Category", "Unit")]
public class MessagingServiceCollectionExtensionsTests
{
    [MessageType("order.created.test")]
    private sealed record OrderCreated(string Id) : IMessage;

    private sealed class OrderCreatedHandler : IMessageHandler<OrderCreated>
    {
        public ValueTask<Result> HandleAsync(OrderCreated message, MessageContext context, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Result.Success());
        }
    }

    private sealed class CustomTestMiddleware : IMessageMiddleware
    {
        public ValueTask<Result> InvokeAsync(MessageContext context, MessageExecutionDelegate next, CancellationToken cancellationToken)
        {
            return next(context, cancellationToken);
        }
    }

    [Fact]
    public void AddMessaging_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection? services = null;
        Action act = () => services!.AddMessaging();
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddMessaging_WithoutConfigureAction_RegistersDefaultsSuccessfully()
    {
        var services = new ServiceCollection();

        // Act
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddMessaging();

        // Assert
        using var sp = services.BuildServiceProvider();
        sp.GetService<IMessageSerializer>().Should().BeOfType<NativeAotJsonSerializer>();
        sp.GetService<IMessageTransport>().Should().BeOfType<EricksonLopez.Messaging.Transport.InMemory.InMemoryMessageTransport>();
        sp.GetService<DefaultMessageDispatcher>().Should().NotBeNull();
        sp.GetService<IMessageDispatcher>().Should().NotBeNull();
        sp.GetService<IMessagePublisher>().Should().BeOfType<MessagePublisher>();
        sp.GetService<IMessageConsumer>().Should().BeOfType<MessageConsumer>();
        sp.GetService<IHostedService>().Should().BeOfType<MessagingConsumerHostedService>();
    }

    [Fact]
    public void AddMessaging_WithCustomMiddlewareConfiguration_RegistersAllServices()
    {
        var services = new ServiceCollection();

        // Act
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddMessaging(options =>
        {
            options.Services.Should().BeSameAs(services);
            options.AddMiddleware<CustomTestMiddleware>();
        });

        // Assert
        using var sp = services.BuildServiceProvider();
        sp.GetService<IMessageSerializer>().Should().BeOfType<NativeAotJsonSerializer>();
        sp.GetService<IMessageTransport>().Should().BeOfType<EricksonLopez.Messaging.Transport.InMemory.InMemoryMessageTransport>();
        sp.GetService<DefaultMessageDispatcher>().Should().NotBeNull();
        sp.GetService<IMessageDispatcher>().Should().NotBeNull();
        sp.GetService<IMessagePublisher>().Should().BeOfType<MessagePublisher>();
        sp.GetService<IMessageConsumer>().Should().BeOfType<MessageConsumer>();
        sp.GetService<IHostedService>().Should().BeOfType<MessagingConsumerHostedService>();

        var middlewares = sp.GetServices<IMessageMiddleware>();
        middlewares.Should().Contain(m => m is CustomTestMiddleware);
        middlewares.Should().Contain(m => m is TracingMiddleware);
        middlewares.Should().Contain(m => m is LoggingMiddleware);
        middlewares.Should().Contain(m => m is ExceptionHandlingMiddleware);
    }

    [Fact]
    public void MessagingOptionsBuilder_AddCircuitBreaker_RegistersMiddlewareAndConfiguresOptions()
    {
        var services = new ServiceCollection();

        // Act - configure with lambda
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddMessaging(options =>
        {
            options.AddCircuitBreaker(opts =>
            {
                opts.FailureThreshold = 10;
                opts.SamplingDuration = TimeSpan.FromSeconds(60);
                opts.BreakDuration = TimeSpan.FromSeconds(45);
            });
        });

        // Assert
        using var sp = services.BuildServiceProvider();
        var middlewares = sp.GetServices<IMessageMiddleware>();
        middlewares.Should().Contain(m => m is CircuitBreakerMiddleware);

        var cbOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CircuitBreakerOptions>>().Value;
        cbOptions.FailureThreshold.Should().Be(10);
        cbOptions.SamplingDuration.Should().Be(TimeSpan.FromSeconds(60));
        cbOptions.BreakDuration.Should().Be(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void MessagingOptionsBuilder_AddCircuitBreaker_NullAction_RegistersMiddlewareWithDefaults()
    {
        var services = new ServiceCollection();

        // Act - configure with null
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddMessaging(options =>
        {
            options.AddCircuitBreaker(null);
        });

        // Assert
        using var sp = services.BuildServiceProvider();
        var middlewares = sp.GetServices<IMessageMiddleware>();
        middlewares.Should().Contain(m => m is CircuitBreakerMiddleware);
    }

    [Fact]
    public void MessagingOptionsBuilder_AddHandlerTimeout_WithTimeSpan_RegistersMiddlewareAndConfiguresTimeout()
    {
        var services = new ServiceCollection();

        // Act
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddMessaging(options =>
        {
            options.AddHandlerTimeout(TimeSpan.FromSeconds(42));
        });

        // Assert
        using var sp = services.BuildServiceProvider();
        var middlewares = sp.GetServices<IMessageMiddleware>();
        middlewares.Should().Contain(m => m is HandlerTimeoutMiddleware);

        var timeoutOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HandlerTimeoutOptions>>().Value;
        timeoutOptions.Timeout.Should().Be(TimeSpan.FromSeconds(42));
    }

    [Fact]
    public void MessagingOptionsBuilder_AddHandlerTimeout_WithAction_RegistersMiddlewareAndConfiguresOptions()
    {
        var services = new ServiceCollection();

        // Act
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddMessaging(options =>
        {
            options.AddHandlerTimeout(opts =>
            {
                opts.Timeout = TimeSpan.FromSeconds(15);
            });
        });

        // Assert
        using var sp = services.BuildServiceProvider();
        var middlewares = sp.GetServices<IMessageMiddleware>();
        middlewares.Should().Contain(m => m is HandlerTimeoutMiddleware);

        var timeoutOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HandlerTimeoutOptions>>().Value;
        timeoutOptions.Timeout.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void MessagingOptionsBuilder_AddHandlerTimeout_WithNullAction_RegistersMiddlewareWithDefaults()
    {
        var services = new ServiceCollection();

        // Act
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddMessaging(options =>
        {
            options.AddHandlerTimeout((Action<HandlerTimeoutOptions>?)null);
        });

        // Assert
        using var sp = services.BuildServiceProvider();
        var middlewares = sp.GetServices<IMessageMiddleware>();
        middlewares.Should().Contain(m => m is HandlerTimeoutMiddleware);
    }

    [Fact]
    public void MessagingOptionsBuilder_AddUpcasting_RegistersMessageUpcastingMiddleware()
    {
        var services = new ServiceCollection();

        // Act
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddMessaging(options =>
        {
            options.AddUpcasting();
        });

        // Assert
        using var sp = services.BuildServiceProvider();
        var middlewares = sp.GetServices<IMessageMiddleware>();
        middlewares.Should().Contain(m => m is MessageUpcastingMiddleware);
    }

    [Fact]
    public void AddMessageHandler_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection? services = null;
        Action act = () => services!.AddMessageHandler<OrderCreated, OrderCreatedHandler>();
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddMessageHandler_WithValidHandler_RegistersHandlerAndRegistrationObject()
    {
        var services = new ServiceCollection();
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddMessaging();

        // Act
        services.AddMessageHandler<OrderCreated, OrderCreatedHandler>();

        // Assert
        using var sp = services.BuildServiceProvider();
        using (var scope = sp.CreateScope())
        {
            var handler = scope.ServiceProvider.GetService<IMessageHandler<OrderCreated>>();
            handler.Should().NotBeNull();
            handler.Should().BeOfType<OrderCreatedHandler>();

            var concreteHandler = scope.ServiceProvider.GetService<OrderCreatedHandler>();
            concreteHandler.Should().NotBeNull();
        }

        var registrations = sp.GetServices<IHandlerRegistration>();
        registrations.OfType<HandlerRegistrationBase>().Should().Contain(baseReg => baseReg.TypeName == "order.created.test");

        var dispatcher = sp.GetRequiredService<DefaultMessageDispatcher>();
        foreach (var reg in registrations)
        {
            if (reg is HandlerRegistrationBase baseReg)
            {
                baseReg.Register(dispatcher);
            }
        }
    }

    private sealed record OrderCreatedV1(string Id) : IMessage;
    private sealed record OrderCreatedV2(string Id, string CustomerId) : IMessage;

    private sealed class OrderCreatedUpcaster : IMessageUpcaster<OrderCreatedV1, OrderCreatedV2>
    {
        public OrderCreatedV2 Upcast(OrderCreatedV1 oldMessage, TransportMessageMetadata metadata)
        {
            return new OrderCreatedV2(oldMessage.Id, "default-customer");
        }
    }

    [Fact]
    public void AddMessageUpcaster_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection? services = null;
        Action act = () => services!.AddMessageUpcaster<OrderCreatedV1, OrderCreatedV2, OrderCreatedUpcaster>();
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddMessageUpcaster_ValidUpcaster_RegistersUpcasterAndInvoker()
    {
        var services = new ServiceCollection();
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddMessaging();

        // Act
        services.AddMessageUpcaster<OrderCreatedV1, OrderCreatedV2, OrderCreatedUpcaster>();

        // Assert
        using var sp = services.BuildServiceProvider();
        using (var scope = sp.CreateScope())
        {
            var upcaster = scope.ServiceProvider.GetService<OrderCreatedUpcaster>();
            upcaster.Should().NotBeNull();
        }

        var invoker = sp.GetService<IMessageUpcasterInvoker>();
        invoker.Should().NotBeNull();
        invoker.Should().BeOfType<MessageUpcasterInvoker<OrderCreatedV1, OrderCreatedV2, OrderCreatedUpcaster>>();
    }
}



