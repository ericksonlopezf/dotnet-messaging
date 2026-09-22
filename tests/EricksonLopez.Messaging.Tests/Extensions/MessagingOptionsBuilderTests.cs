// Copyright © Erickson Lopez. MIT License.
using System;
using System.Linq;
using System.Reflection;
using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Dispatch;
using EricksonLopez.Messaging.Extensions;
using EricksonLopez.Messaging.Middleware;
using EricksonLopez.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EricksonLopez.Messaging.Tests.Extensions;

[Trait("Category", "Unit")]
public sealed class MessagingOptionsBuilderTests
{
    [Fact]
    public void AddRetry_WithCustomOptions_ConfiguresRetryMiddlewareOptions()
    {
        var services = new ServiceCollection();
        var builder = new MessagingOptionsBuilder(services);

        builder.AddRetry(opts =>
        {
            opts.MaxRetries = 7;
            opts.InitialDelay = TimeSpan.FromSeconds(2);
        });

        var sp = services.BuildServiceProvider();
        var middleware = sp.GetServices<IMessageMiddleware>().OfType<RetryMiddleware>().SingleOrDefault();
        middleware.Should().NotBeNull();

        var retriesField = typeof(RetryMiddleware).GetField("_maxRetries", BindingFlags.NonPublic | BindingFlags.Instance);
        retriesField!.GetValue(middleware).Should().Be(7);

        var delayField = typeof(RetryMiddleware).GetField("_initialDelay", BindingFlags.NonPublic | BindingFlags.Instance);
        delayField!.GetValue(middleware).Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void AddExceptionHandling_IsIdempotent()
    {
        var services = new ServiceCollection();
        var builder = new MessagingOptionsBuilder(services);

        builder.AddExceptionHandling();
        builder.AddExceptionHandling();

        services.Count(d => d.ServiceType == typeof(IMessageMiddleware) && d.ImplementationType == typeof(ExceptionHandlingMiddleware))
            .Should().Be(1);
    }

    [Fact]
    public void AddLogging_IsIdempotent()
    {
        var services = new ServiceCollection();
        var builder = new MessagingOptionsBuilder(services);

        builder.AddLogging();
        builder.AddLogging();

        services.Count(d => d.ServiceType == typeof(IMessageMiddleware) && d.ImplementationType == typeof(LoggingMiddleware))
            .Should().Be(1);
    }

    [Fact]
    public void AddTracing_IsIdempotent_AndDistinctFromOtherMiddlewares()
    {
        var services = new ServiceCollection();
        var builder = new MessagingOptionsBuilder(services);

        builder.AddLogging();
        builder.AddTracing();
        builder.AddTracing();

        services.Count(d => d.ServiceType == typeof(IMessageMiddleware) && d.ImplementationType == typeof(TracingMiddleware))
            .Should().Be(1);
    }

    [Fact]
    public void AddCircuitBreaker_WithConfigure_RegistersConfiguredOptions()
    {
        var services = new ServiceCollection();
        var builder = new MessagingOptionsBuilder(services);

        builder.AddCircuitBreaker(opts => opts.FailureThreshold = 10);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<CircuitBreakerOptions>>().Value;
        options.FailureThreshold.Should().Be(10);

        services.Any(d => d.ServiceType == typeof(IMessageMiddleware) && d.ImplementationType == typeof(CircuitBreakerMiddleware))
            .Should().BeTrue();
    }

    [Fact]
    public void AddHandlerTimeout_WithExplicitTimeSpan_RegistersConfiguredTimeout()
    {
        var services = new ServiceCollection();
        var builder = new MessagingOptionsBuilder(services);

        builder.AddHandlerTimeout(TimeSpan.FromSeconds(45));

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<HandlerTimeoutOptions>>().Value;
        options.Timeout.Should().Be(TimeSpan.FromSeconds(45));

        services.Any(d => d.ServiceType == typeof(IMessageMiddleware) && d.ImplementationType == typeof(HandlerTimeoutMiddleware))
            .Should().BeTrue();
    }

    [Fact]
    public void AddHandlerTimeout_WithConfigureAction_RegistersConfiguredTimeout()
    {
        var services = new ServiceCollection();
        var builder = new MessagingOptionsBuilder(services);

        builder.AddHandlerTimeout(opts => opts.Timeout = TimeSpan.FromSeconds(50));

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<HandlerTimeoutOptions>>().Value;
        options.Timeout.Should().Be(TimeSpan.FromSeconds(50));
    }

    [Fact]
    public void AddUpcasting_RegistersMessageUpcastingMiddleware()
    {
        var services = new ServiceCollection();
        var builder = new MessagingOptionsBuilder(services);

        builder.AddUpcasting();

        services.Any(d => d.ServiceType == typeof(IMessageMiddleware) && d.ImplementationType == typeof(MessageUpcastingMiddleware))
            .Should().BeTrue();
    }

    [Fact]
    public void AddDeduplication_RegistersStoreAndMiddlewareWithOptions()
    {
        var services = new ServiceCollection();
        var builder = new MessagingOptionsBuilder(services);

        builder.AddDeduplication(opts => opts.Expiration = TimeSpan.FromHours(4));

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<MessageDeduplicationOptions>>().Value;
        options.Expiration.Should().Be(TimeSpan.FromHours(4));

        var store = sp.GetRequiredService<IMessageDeduplicationStore>();
        store.Should().BeOfType<InMemoryMessageDeduplicationStore>();

        services.Any(d => d.ServiceType == typeof(IMessageMiddleware) && d.ImplementationType == typeof(MessageDeduplicationMiddleware))
            .Should().BeTrue();
    }

    [Fact]
    public void AddDeduplication_WithNullConfigure_RegistersDefaults()
    {
        var services = new ServiceCollection();
        var builder = new MessagingOptionsBuilder(services);

        builder.AddDeduplication(null);

        var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IMessageDeduplicationStore>();
        store.Should().BeOfType<InMemoryMessageDeduplicationStore>();
    }

    [Fact]
    public void ConfigureConsumer_WithValidDelegate_ConfiguresOptions()
    {
        var services = new ServiceCollection();
        var builder = new MessagingOptionsBuilder(services);

        builder.ConfigureConsumer(opts => opts.UnhandledFailureAckResult = TransportAckResult.NackRequeue);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<MessageConsumerOptions>>().Value;
        options.UnhandledFailureAckResult.Should().Be(TransportAckResult.NackRequeue);
    }

    [Fact]
    public void ConfigureConsumer_NullDelegate_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var builder = new MessagingOptionsBuilder(services);

        var act = () => builder.ConfigureConsumer(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configure");
    }
}
