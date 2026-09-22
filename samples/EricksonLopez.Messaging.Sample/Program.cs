// Copyright © Erickson Lopez. MIT License.
// =============================================================================
// EricksonLopez.Messaging — Official Reference Showcase
// =============================================================================
// This file is the canonical executable documentation for EricksonLopez.Messaging.
// It serves simultaneously as:
//   • Official API Reference
//   • Quick Start Guide
//   • Integration Cookbook
//   • Learning Path (L0 → L10)
//   • Validation that every public API is covered and compilable.
//
// LEARNING LEVELS
//  L0  — Conceptual overview
//  L1  — Quick start / minimal setup
//  L2  — Full configuration & builder
//  L3  — Core use-cases (pub/sub, send, batch)
//  L4  — Integration patterns (Events bridge, transport swap)
//  L5  — Background processing & hosting lifecycle
//  L6  — Error handling (retry, circuit breaker, dead-letter, functional Result)
//  L7  — Scalability (concurrency, batch transport)
//  L8  — Customisation (custom middleware, custom transport, custom serializer)
//  L9  — Broker extensions (RabbitMQ, Kafka, Azure Service Bus, AWS SQS)
//  L10 — Enterprise patterns (OpenTelemetry, health checks, upcasting, partition key)
//
// SOURCE OF TRUTH: Only APIs that exist in the library are used here.
// =============================================================================

namespace EricksonLopez.Messaging.Sample;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EricksonLopez.Messaging;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Diagnostics;
using EricksonLopez.Messaging.Dispatch;
using EricksonLopez.Messaging.Events;
using EricksonLopez.Messaging.HealthChecks;
using EricksonLopez.Messaging.Middleware;
using EricksonLopez.Messaging.Serialization;
using EricksonLopez.Messaging.Testing;
using EricksonLopez.Messaging.Transport;
using EricksonLopez.Messaging.Transport.InMemory;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;


// =============================================================================
// ENTRY POINT
// =============================================================================

/// <summary>
/// Provides the reference implementation and executable documentation showcase for EricksonLopez.Messaging.
/// Demonstrates every public API across progressive learning levels L0–L10.
/// </summary>
public static class Program
{
    /// <summary>
    /// Executes the reference showcase covering all progressive learning levels.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task Main(string[] args)
    {
        PrintBanner();

        // Run all showcase sections in order.
        await RunLevel0_ConceptualAsync();
        await RunLevel1_QuickStartAsync();
        await RunLevel2_FullConfigurationAsync();
        await RunLevel3_CoreUseCasesAsync();
        await RunLevel4_IntegrationPatternsAsync();
        await RunLevel5_BackgroundProcessingAsync();
        await RunLevel6_ErrorHandlingAsync();
        await RunLevel7_ScalabilityAsync();
        await RunLevel8_CustomisationAsync();
        RunLevel9_BrokerExtensionConfiguration();
        await RunLevel10_EnterpriseAsync();
        await RunLevel11_AdvancedDispatchAndTransportInternalsAsync();

        Console.WriteLine();
        Console.WriteLine("========================================================");
        Console.WriteLine(" Showcase complete. All APIs demonstrated successfully.");
        Console.WriteLine("========================================================");
    }

    // =========================================================================
    // L0 — CONCEPTUAL OVERVIEW
    // =========================================================================
    // What is EricksonLopez.Messaging?
    //   A strongly-typed, transport-agnostic distributed messaging library for
    //   .NET 10, built for Native AOT, functional error handling (Result<T>),
    //   and progressive complexity from in-memory testing to enterprise brokers.
    //
    // What problem does it solve?
    //   • Decouples producers from consumers via message types, not direct calls.
    //   • Unifies pub/sub (1:N) and point-to-point (1:1) messaging patterns.
    //   • Provides a single middleware pipeline for cross-cutting concerns.
    //   • Transport is swappable without changing handler or publisher code.
    //
    // Why does it exist?
    //   To provide a first-class messaging abstraction for the EricksonLopez
    //   ecosystem that integrates naturally with Microsoft.Extensions.Hosting,
    //   OpenTelemetry, and the Result monad pattern.
    //
    // Advantages:
    //   ✔ Native AOT compatible
    //   ✔ Functional error handling — no exceptions for expected failures
    //   ✔ Zero-allocation serialization path via IBufferWriter<byte>
    //   ✔ Pluggable transport (InMemory, RabbitMQ, Kafka, Azure SB, AWS SQS)
    //   ✔ Built-in middleware: Tracing, Logging, Exception, CircuitBreaker, Retry,
    //     HandlerTimeout, MessageUpcasting
    //   ✔ Message schema evolution via Upcasters
    //   ✔ OpenTelemetry distributed tracing and metrics out-of-the-box
    //   ✔ Test harness included (InMemoryTestHarness)
    //
    // Disadvantages / Limitations:
    //   ✘ IDeferableMessageTransport is only supported by brokers that natively
    //     support deferred/scheduled delivery (not InMemory transport).
    //   ✘ No built-in outbox pattern; consumers must implement their own.
    //   ✘ Dead-letter queue interface (IDeadLetterQueue) requires application
    //     implementation; no default DLQ transport adapter is included.
    //
    // Comparison with alternatives:
    //   MassTransit   — heavier, more convention over configuration.
    //   NServiceBus   — commercial; feature-rich saga support.
    //   Rebus         — lighter-weight, fewer first-party transports.
    //   EricksonLopez.Messaging — minimal surface, AOT-first, Result-native.
    // =========================================================================

    private static Task RunLevel0_ConceptualAsync()
    {
        Section("L0 — Conceptual Overview");

        // MessagingDiagnostics exposes the canonical ActivitySource and Meter names.
        Console.WriteLine($"[L0] ActivitySource : {MessagingDiagnostics.ActivitySourceName}");
        Console.WriteLine($"[L0] Meter          : {MessagingDiagnostics.MeterName}");
        Console.WriteLine($"[L0] Version        : {MessagingDiagnostics.Version}");

        return Task.CompletedTask;
    }

    // =========================================================================
    // L1 — QUICK START: Minimal viable setup
    // =========================================================================

    private static async Task RunLevel1_QuickStartAsync()
    {
        Section("L1 — Quick Start");

        // Minimum configuration: call AddMessaging() with no arguments.
        // This registers:
        //   • IMessageSerializer  → NativeAotJsonSerializer
        //   • IMessageTransport   → InMemoryMessageTransport
        //   • IMessageDispatcher  → DefaultMessageDispatcher
        //   • IMessagePublisher   → MessagePublisher
        //   • IMessageConsumer    → MessageConsumer
        //   • IHostedService      → MessagingConsumerHostedService
        //   • Default middlewares → TracingMiddleware, LoggingMiddleware,
        //                           ExceptionHandlingMiddleware

        // NOTE: AddMessageHandler must be called on IServiceCollection *before* host.Build().
        //       The correct pattern is shown in L2 (RunLevel2_FullConfigurationAsync).
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMessaging();
        builder.Services.AddMessageHandler<PingMessage, PingHandler>();
        var host = builder.Build();

        await host.StartAsync();

        var publisher = host.Services.GetRequiredService<IMessagePublisher>();
        var result = await publisher.PublishAsync(new PingMessage("hello"));

        PrintResult("L1 PublishAsync (minimal)", result);

        await host.StopAsync();
    }

    // =========================================================================
    // L2 — FULL CONFIGURATION
    // =========================================================================

    private static async Task RunLevel2_FullConfigurationAsync()
    {
        Section("L2 — Full Configuration");

        // MessagingOptionsBuilder fluent API:
        //   .AddCircuitBreaker(configure?)       — CircuitBreakerMiddleware
        //   .AddHandlerTimeout(TimeSpan)          — HandlerTimeoutMiddleware (overload 1)
        //   .AddHandlerTimeout(configure?)        — HandlerTimeoutMiddleware (overload 2)
        //   .AddUpcasting()                       — MessageUpcastingMiddleware
        //   .AddMiddleware<TMiddleware>()          — custom IMessageMiddleware

        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddMessaging(opts =>
        {
            // Circuit Breaker: opens after 3 consecutive failures, resets after 10 s
            opts.AddCircuitBreaker(cb =>
            {
                cb.FailureThreshold = 3;
                cb.BreakDuration = TimeSpan.FromSeconds(10);
                cb.SamplingDuration = TimeSpan.FromSeconds(30);
                cb.TimeProvider = TimeProvider.System;
            });

            // Handler Timeout overload 1: explicit TimeSpan
            opts.AddHandlerTimeout(TimeSpan.FromSeconds(5));

            // Handler Timeout overload 2: configure delegate
            opts.AddHandlerTimeout(o =>
            {
                o.Timeout = TimeSpan.FromSeconds(8);
                o.TimeProvider = TimeProvider.System;
            });

            // AddRetry — register RetryMiddleware via builder (with full RetryOptions)
            opts.AddRetry(r =>
            {
                r.MaxRetries = 5;
                r.InitialDelay = TimeSpan.FromMilliseconds(200);
                r.MaxDelay = TimeSpan.FromSeconds(30);    // cap exponential backoff at 30 s
                r.ShouldRetry = err => err.Code != "Messaging.HandlerNotFound"; // skip retry for routing errors
                r.TimeProvider = TimeProvider.System;
            });

            // Upcasting middleware — required for schema evolution (L10)
            opts.AddUpcasting();

            // Deduplication middleware configuration
            opts.AddDeduplication(d =>
            {
                d.Expiration = TimeSpan.FromMinutes(5);
                d.Enabled = true; // set to false to disable deduplication entirely
            });

            // Consumer tuning configuration (MessageConsumerOptions)
            opts.ConfigureConsumer(c =>
            {
                c.MaxConcurrency = 4;
                c.PrefetchCount = 10;
                c.UnhandledFailureAckResult = TransportAckResult.DeadLetter;
            });

            // Custom middleware — demonstrated in L8
            opts.AddMiddleware<AuditMiddleware>();
        });

        // ---------------------------------------------------------------
        // L2-B: MessagingOptionsBuilder standalone methods
        //        AddLogging(), AddExceptionHandling(), AddTracing() are each
        //        an explicit builder method to control pipeline order when
        //        building a custom pipeline without the AddMessaging() defaults.
        // ---------------------------------------------------------------
        var builder2 = Host.CreateApplicationBuilder();
        builder2.Services.AddMessaging(opts2 =>
        {
            // Demonstrate the three standalone pipeline control methods:
            //   .AddTracing()            — register TracingMiddleware at this position
            //   .AddLogging()            — register LoggingMiddleware at this position
            //   .AddExceptionHandling()  — register ExceptionHandlingMiddleware here
            opts2.AddTracing();
            opts2.AddLogging();
            opts2.AddExceptionHandling();
        });
        var app2b = builder2.Build(); // verify DI composition is valid
        Console.WriteLine("[L2-B] AddTracing() + AddLogging() + AddExceptionHandling() — builder calls OK.");
        _ = app2b; // suppress unused-variable warning

        // Health check extension
        builder.Services.AddMessagingHealthCheck();

        // Handler registration
        builder.Services.AddMessageHandler<OrderPlacedEventV1, OrderPlacedV1Handler>();
        builder.Services.AddMessageHandler<OrderPlacedEventV2, OrderPlacedV2Handler>();
        builder.Services.AddMessageHandler<ProcessPaymentCommand, ProcessPaymentHandler>();
        builder.Services.AddMessageHandler<InventoryItemUpdatedEvent, InventoryItemUpdatedHandler>();
        builder.Services.AddMessageHandler<ValidateCustomerMessage, ValidateCustomerHandler>();
        builder.Services.AddMessageHandler<PingMessage, PingHandler>();
        builder.Services.AddMessageHandler<BatchItemMessage, BatchItemHandler>();
        builder.Services.AddMessageHandler<NotificationMessage, NotificationHandler>();
        builder.Services.AddMessageHandler<ScheduledReportMessage, ScheduledReportHandler>();

        // Upcaster registration
        builder.Services.AddMessageUpcaster<OrderPlacedEventV1, OrderPlacedEventV2, OrderPlacedUpcaster>();

        // OpenTelemetry instrumentation (tracing + metrics)
        builder.Services.AddOpenTelemetry()
            .WithTracing(t =>
            {
                t.AddMessagingInstrumentation(); // TracerProviderBuilder extension
                t.AddConsoleExporter();
            })
            .WithMetrics(m =>
            {
                m.AddMessagingInstrumentation(); // MeterProviderBuilder extension
                m.AddConsoleExporter();
            });

        var app = builder.Build();

        // Verify health check is registered
        var healthCheck = app.Services.GetRequiredService<IHealthCheck>(); // MessagingHealthCheck
        var healthResult = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        Console.WriteLine($"[L2] Health: {healthResult.Status} — {healthResult.Description}");

        // InMemoryTransportOptions defaults
        Console.WriteLine($"[L2] InMemoryTransportOptions.ChannelCapacity default: {new InMemoryTransportOptions().ChannelCapacity}");
        Console.WriteLine($"[L2] InMemoryTransportOptions.FullMode default: {new InMemoryTransportOptions().FullMode}");

        // TransportSubscriptionOptions
        var subOpts = new TransportSubscriptionOptions
        {
            MaxConcurrency = Environment.ProcessorCount * 2,
            PrefetchCount = 20,
            ConsumerGroup = "showcase-group"
        };
        Console.WriteLine($"[L2] TransportSubscriptionOptions.MaxConcurrency: {subOpts.MaxConcurrency}");
        Console.WriteLine($"[L2] TransportSubscriptionOptions.ConsumerGroup  : {subOpts.ConsumerGroup}");

        await app.StartAsync();
        await app.StopAsync();
    }

    // =========================================================================
    // L3 — CORE USE CASES
    // =========================================================================

    private static async Task RunLevel3_CoreUseCasesAsync()
    {
        Section("L3 — Core Use Cases");

        var app = BuildMinimalHost(
            s => s.AddMessageHandler<OrderPlacedEventV2, OrderPlacedV2Handler>()
                  .AddMessageHandler<ProcessPaymentCommand, ProcessPaymentHandler>()
                  .AddMessageHandler<InventoryItemUpdatedEvent, InventoryItemUpdatedHandler>()
                  .AddMessageHandler<NotificationMessage, NotificationHandler>()
                  .AddMessageHandler<BatchItemMessage, BatchItemHandler>()
                  .AddMessageHandler<OrderFulfilledEvent, OrderFulfilledHandler>()
        );

        await app.StartAsync();
        var publisher = app.Services.GetRequiredService<IMessagePublisher>();

        // ---------------------------------------------------------------
        // L3-A: PublishAsync<TMessage>(message, options?, ct?)
        //        1:N pub/sub with full MessagePublishOptions
        // ---------------------------------------------------------------
        var publishOptions = new MessagePublishOptions
        {
            CorrelationId = "corr-l3-001",
            CausationId = "caus-showcase",
            TenantId = "tenant-acme",
            PartitionKey = "EU-WEST",
            Destination = null,         // null = derive from [MessageType]
            Headers = new Dictionary<string, string>
            {
                ["X-Region"] = "eu-west-1",
                ["X-Priority"] = "Normal"
            }
        };

        var orderEvent = new OrderPlacedEventV2(
            Guid.NewGuid(), "ORD-L3-001", 799.99m, "EUR", "Mobile-App");

        var r1 = await publisher.PublishAsync(orderEvent, publishOptions);
        PrintResult("L3-A PublishAsync (with options)", r1);

        // L3-A2: PublishAsync without options (simplest overload)
        var r1b = await publisher.PublishAsync(new NotificationMessage(Guid.NewGuid(), "Hello, World!"));
        PrintResult("L3-A2 PublishAsync (no options)", r1b);

        // L3-A3: OrderFulfilledEvent — demonstrates [MessageType] + IMessage + IMessageHandler contract
        var fulfilledEvent = new OrderFulfilledEvent(
            Guid.NewGuid(), "ORD-L3-FULFILLED", 1250.00m);
        var r1c = await publisher.PublishAsync(fulfilledEvent);
        PrintResult("L3-A3 PublishAsync OrderFulfilledEvent", r1c);

        // ---------------------------------------------------------------
        // L3-B: SendAsync<TMessage>(message, destination, options?, ct?)
        //        1:1 point-to-point with MessageSendOptions
        // ---------------------------------------------------------------
        var sendOptions = new MessageSendOptions
        {
            CorrelationId = "corr-l3-002",
            CausationId = "caus-order-service",
            TenantId = "tenant-acme",
            PartitionKey = "payment-shard-1",
            Headers = new Dictionary<string, string> { ["X-Trace"] = "abc123" }
        };

        var paymentCmd = new ProcessPaymentCommand(
            Guid.NewGuid(), "ORD-L3-001", 799.99m, "EUR");

        var r2 = await publisher.SendAsync(paymentCmd, "payments.process.v1", sendOptions);
        PrintResult("L3-B SendAsync (with options)", r2);

        // L3-B2: SendAsync without options
        var r2b = await publisher.SendAsync(
            new ProcessPaymentCommand(Guid.NewGuid(), "ORD-L3-002", 10m, "USD"),
            "payments.process.v1");
        PrintResult("L3-B2 SendAsync (no options)", r2b);

        // ---------------------------------------------------------------
        // L3-C: PublishBatchAsync<TMessage>(messages, options?, ct?)
        //        Efficient 1:N batch publishing
        // ---------------------------------------------------------------
        var inventoryBatch = new List<InventoryItemUpdatedEvent>
        {
            new("SKU-001", 100, 29.99m),
            new("SKU-002",  50, 49.99m),
            new("SKU-003", 200,  9.99m),
        };

        var r3 = await publisher.PublishBatchAsync(inventoryBatch);
        PrintResult("L3-C PublishBatchAsync", r3);

        // L3-C2: PublishBatchAsync with options
        var r3b = await publisher.PublishBatchAsync(
            inventoryBatch,
            new MessagePublishOptions { TenantId = "tenant-acme" });
        PrintResult("L3-C2 PublishBatchAsync (with options)", r3b);

        // ---------------------------------------------------------------
        // L3-D: SendBatchAsync<TMessage>(messages, destination, options?, ct?)
        //        Efficient 1:1 batch sending
        // ---------------------------------------------------------------
        var commandBatch = new List<BatchItemMessage>
        {
            new(Guid.NewGuid(), "process-a"),
            new(Guid.NewGuid(), "process-b"),
        };

        var r4 = await publisher.SendBatchAsync(commandBatch, "batch.items.v1");
        PrintResult("L3-D SendBatchAsync", r4);

        // L3-D2: SendBatchAsync with options
        var r4b = await publisher.SendBatchAsync(
            commandBatch,
            "batch.items.v1",
            new MessageSendOptions { CorrelationId = "corr-batch-001" });
        PrintResult("L3-D2 SendBatchAsync (with options)", r4b);

        // ---------------------------------------------------------------
        // L3-E: MessageEnvelope<TMessage> — typed payload carrier
        // ---------------------------------------------------------------
        var envelope = MessageEnvelope<NotificationMessage>.Create(
            payload: new NotificationMessage(Guid.NewGuid(), "Envelope demo"),
            messageType: "notifications.v1",
            correlationId: "corr-env-001",
            causationId: "caus-env-source",
            tenantId: "tenant-acme",
            partitionKey: "shard-1",
            schemaVersion: 2);

        Console.WriteLine($"[L3-E] Envelope MessageId : {envelope.Metadata.MessageId}");
        Console.WriteLine($"[L3-E] Envelope MessageType: {envelope.Metadata.MessageType}");
        Console.WriteLine($"[L3-E] Envelope SchemaVersion: {envelope.Metadata.SchemaVersion}");
        Console.WriteLine($"[L3-E] Envelope ContentType: {envelope.Metadata.ContentType}");

        // ---------------------------------------------------------------
        // L3-F: TransportMessageMetadata.Create() factory
        // ---------------------------------------------------------------
        var metadata = TransportMessageMetadata.Create(
            messageType: "orders.placed.v2",
            correlationId: "corr-meta-001",
            causationId: "caus-meta-source",
            tenantId: "tenant-acme",
            partitionKey: "EU",
            schemaVersion: 2,
            headers: new Dictionary<string, string> { ["X-Custom"] = "yes" });

        Console.WriteLine($"[L3-F] Metadata.MessageId       : {metadata.MessageId}");
        Console.WriteLine($"[L3-F] Metadata.Timestamp       : {metadata.Timestamp:O}");
        Console.WriteLine($"[L3-F] Metadata.CorrelationId   : {metadata.CorrelationId}");
        Console.WriteLine($"[L3-F] Metadata.TenantId        : {metadata.TenantId}");
        Console.WriteLine($"[L3-F] Metadata.PartitionKey    : {metadata.PartitionKey}");
        Console.WriteLine($"[L3-F] Metadata.CausationId     : {metadata.CausationId}");
        Console.WriteLine($"[L3-F] Metadata.SchemaVersion   : {metadata.SchemaVersion}");
        Console.WriteLine($"[L3-F] Metadata.ContentType     : {metadata.ContentType}");

        await app.StopAsync();
    }

    // =========================================================================
    // L4 — INTEGRATION PATTERNS
    // =========================================================================

    private static async Task RunLevel4_IntegrationPatternsAsync()
    {
        Section("L4 — Integration Patterns");

        // ---------------------------------------------------------------
        // L4-A: MessagingEventPublisher bridge (EricksonLopez.Messaging.Events)
        //        Connects IEventPublisher from EricksonLopez.Events.Contracts
        //        to IMessagePublisher so domain events flow through the bus.
        // ---------------------------------------------------------------

        // MessagingEventsOptions
        var eventsOptions = new MessagingEventsOptions
        {
            ThrowOnFailure = false,
            DestinationResolver = type => $"{type.Name.ToLowerInvariant()}.v1"
        };
        Console.WriteLine($"[L4-A] MessagingEventsOptions.ThrowOnFailure: {eventsOptions.ThrowOnFailure}");
        Console.WriteLine($"[L4-A] DestinationResolver demo: {eventsOptions.DestinationResolver(typeof(OrderPlacedEventV2))}");

        // Service-container based wiring:
        //   builder.Services.AddMessaging(...);
        //   builder.Services.AddMessagingEventPublisher(opts => { opts.ThrowOnFailure = false; });
        // This registers MessagingEventPublisher as IEventPublisher (scoped).
        Console.WriteLine("[L4-A] AddMessagingEventPublisher registers MessagingEventPublisher as IEventPublisher.");
        Console.WriteLine("       See L9 broker section for full wiring with real IEventPublisher usage.");

        // ---------------------------------------------------------------
        // L4-B: [PartitionKeyAttribute] — marks a property as partition key
        // ---------------------------------------------------------------
        // The attribute is scanned by source generator (EricksonLopez.Messaging.Generators)
        // to produce deterministic routing hints for brokers that support partitioning.
        // At runtime it has no direct runtime behavior; the partition key is instead
        // supplied explicitly via MessagePublishOptions.PartitionKey or
        // MessageSendOptions.PartitionKey.

        // Demonstration: inspect the attribute on a message type at runtime
        var partitionedMsg = new PartitionedOrderMessage(Guid.NewGuid(), "EU-WEST");
        var prop = partitionedMsg.GetType().GetProperty("Region");
        var attr = prop?.GetCustomAttributes(typeof(PartitionKeyAttribute), true);
        Console.WriteLine($"[L4-B] PartitionKeyAttribute on 'Region': {attr?.Length > 0}");

        // ---------------------------------------------------------------
        // L4-C: IMessageDispatcher — manually dispatch a raw payload
        //        (Advanced integration; normally used only by infrastructure code)
        // ---------------------------------------------------------------
        var app = BuildMinimalHost(s => s.AddMessageHandler<PingMessage, PingHandler>());
        await app.StartAsync();

        var dispatcher = app.Services.GetRequiredService<IMessageDispatcher>();
        var serializer = app.Services.GetRequiredService<IMessageSerializer>();

        var ping = new PingMessage("dispatcher-test");
        var payload = serializer.Serialize(ping);
        var meta = TransportMessageMetadata.Create("ping.v1", correlationId: "corr-dispatch-001");
        var ctx = app.Services;

        var dispatchResult = await dispatcher.DispatchAsync(
            messageType: "ping.v1",
            payload: payload,
            metadata: meta,
            serviceProvider: ctx,
            cancellationToken: CancellationToken.None);

        PrintResult("L4-C IMessageDispatcher.DispatchAsync", dispatchResult);

        await app.StopAsync();
    }

    // =========================================================================
    // L5 — BACKGROUND PROCESSING & HOSTING LIFECYCLE
    // =========================================================================

    private static async Task RunLevel5_BackgroundProcessingAsync()
    {
        Section("L5 — Background Processing & Hosting");

        // MessagingConsumerHostedService is registered automatically by AddMessaging().
        // It calls IMessageConsumer.StartAsync on host start and
        // IMessageConsumer.StopReceivingAsync + DrainInFlightMessagesAsync on stop.

        var app = BuildMinimalHost(
            s => s.AddMessageHandler<ScheduledReportMessage, ScheduledReportHandler>()
        );

        await app.StartAsync();

        var consumer = app.Services.GetRequiredService<IMessageConsumer>();
        var publisher = app.Services.GetRequiredService<IMessagePublisher>();

        // IMessageConsumer public lifecycle API
        // StartAsync — called by hosted service on startup
        // StopReceivingAsync — stops accepting new messages
        // DrainInFlightMessagesAsync — waits for in-flight handlers to complete

        Console.WriteLine("[L5] IMessageConsumer.StartAsync called by MessagingConsumerHostedService.");
        Console.WriteLine("[L5] Publishing a message while consumer is active...");

        var r = await publisher.PublishAsync(
            new ScheduledReportMessage(Guid.NewGuid(), "daily-sales", DateTimeOffset.UtcNow));
        PrintResult("L5 PublishAsync (background consumer active)", r);

        // Demonstrate graceful drain
        Console.WriteLine("[L5] Calling StopReceivingAsync...");
        await consumer.StopReceivingAsync();

        Console.WriteLine("[L5] Calling DrainInFlightMessagesAsync...");
        await consumer.DrainInFlightMessagesAsync();

        Console.WriteLine("[L5] Graceful shutdown complete.");

        // MessageConsumer implements IAsyncDisposable and IDisposable.
        // The hosted service disposes it automatically on host shutdown;
        // cast to IAsyncDisposable here for explicit demonstration.
        if (consumer is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
            Console.WriteLine("[L5] MessageConsumer.DisposeAsync called.");
        }

        await app.StopAsync();

        // MessageConsumer.AddDestination — add a destination after construction
        // but before StartAsync is called by the hosted service.
        // (Can only be used when casting to MessageConsumer; shown here for completeness.)
        var builder2 = Host.CreateApplicationBuilder();
        builder2.Services.AddMessaging();
        builder2.Services.AddMessageHandler<PingMessage, PingHandler>();
        var app2 = builder2.Build();

        var consumer2 = app2.Services.GetRequiredService<IMessageConsumer>();
        if (consumer2 is MessageConsumer concreteConsumer)
        {
            concreteConsumer.AddDestination("ping.extra.v1");
            Console.WriteLine("[L5] MessageConsumer.AddDestination('ping.extra.v1') registered.");
        }
        // We don't start app2 to keep the showcase short.
    }

    // =========================================================================
    // L6 — ERROR HANDLING
    // =========================================================================

    private static async Task RunLevel6_ErrorHandlingAsync()
    {
        Section("L6 — Error Handling");

        // ---------------------------------------------------------------
        // L6-A: Functional Result.Failure (no exceptions for expected errors)
        // ---------------------------------------------------------------
        var app = BuildMinimalHost(
            s => s.AddMessageHandler<ValidateCustomerMessage, ValidateCustomerHandler>()
                  .AddMessageHandler<PingMessage, PingHandler>()
        );
        await app.StartAsync();
        var publisher = app.Services.GetRequiredService<IMessagePublisher>();

        var invalidMsg = new ValidateCustomerMessage(Guid.Empty, "");
        var r1 = await publisher.PublishAsync(invalidMsg);
        PrintResult("L6-A Functional validation failure", r1);

        // ---------------------------------------------------------------
        // L6-B: RetryMiddleware with exponential backoff
        //        RetryMiddleware implements IMessageMiddleware directly.
        //        Registration via builder: opts.AddRetry(r => { ... }) — shown in L2.
        //
        // Constructor overload 1: RetryMiddleware(maxRetries, initialDelay?, timeProvider?)
        // Constructor overload 2: RetryMiddleware(RetryOptions)
        // ---------------------------------------------------------------

        // Constructor overload 1: individual parameters
        var retry = new RetryMiddleware(
            maxRetries: 3,
            initialDelay: TimeSpan.FromMilliseconds(50),
            timeProvider: TimeProvider.System);

        // Constructor overload 2: RetryOptions object — includes MaxDelay and ShouldRetry
        var retryViaOptions = new RetryMiddleware(new RetryOptions
        {
            MaxRetries = 2,
            InitialDelay = TimeSpan.FromMilliseconds(75),
            MaxDelay = TimeSpan.FromSeconds(15),                                    // cap backoff
            ShouldRetry = err => err.Code != "Customer.InvalidData",               // skip retry on validation
            TimeProvider = TimeProvider.System
        });
        Console.WriteLine($"[L6-B] RetryMiddleware(RetryOptions) MaxDelay={retryViaOptions.GetType().Name}");
        Console.WriteLine($"[L6-B] RetryOptions.ShouldRetry — predicate set to skip validation errors.");

        // Demonstrate the middleware directly (without full host for brevity)
        var retryContext = new MessageContext(
            TransportMessageMetadata.Create("ping.v1"),
            app.Services,
            CancellationToken.None)
        { Message = new PingMessage("retry-test") };

        var callCount = 0;
        var retryResult = await retry.InvokeAsync(
            retryContext,
            (ctx, ct) =>
            {
                callCount++;
                // Simulate failure on first 2 attempts
                return callCount < 3
                    ? ValueTask.FromResult(Result.Failure(Error.Failure("test.retry", "Transient error")))
                    : ValueTask.FromResult(Result.Success());
            },
            CancellationToken.None);

        Console.WriteLine($"[L6-B] RetryMiddleware attempts: {callCount}, success: {retryResult.IsSuccess}");

        // ---------------------------------------------------------------
        // L6-C: CircuitBreakerMiddleware — configured via builder
        //        CircuitBreakerOptions properties:
        //          FailureThreshold  (default 5)
        //          SamplingDuration  (default 30 s)
        //          BreakDuration     (default 30 s)
        //          TimeProvider      (default TimeProvider.System)
        // ---------------------------------------------------------------
        var cbOptions = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            BreakDuration = TimeSpan.FromSeconds(1),
            SamplingDuration = TimeSpan.FromSeconds(10),
            TimeProvider = TimeProvider.System,
            // FailureFilter — only count specific error codes toward the threshold.
            // When null, all Result failures are counted (default behaviour).
            FailureFilter = err => err.Code != "Customer.InvalidData" // ignore validation errors
        };

        var cbMiddleware = new CircuitBreakerMiddleware(cbOptions);

        // Simulate two consecutive failures to trip the circuit
        for (int i = 0; i < 2; i++)
        {
            var cbCtx = new MessageContext(
                TransportMessageMetadata.Create("ping.v1"),
                app.Services,
                CancellationToken.None)
            { Message = new PingMessage("cb-test") };

            await cbMiddleware.InvokeAsync(
                cbCtx,
                (_, _) => ValueTask.FromResult(Result.Failure(Error.Failure("test.cb", "handler failed"))),
                CancellationToken.None);
        }

        // Third call — circuit should be OPEN
        var cbOpenCtx = new MessageContext(
            TransportMessageMetadata.Create("ping.v1"),
            app.Services,
            CancellationToken.None)
        { Message = new PingMessage("cb-open-test") };

        var cbOpenResult = await cbMiddleware.InvokeAsync(
            cbOpenCtx,
            (_, _) => ValueTask.FromResult(Result.Success()),
            CancellationToken.None);

        Console.WriteLine($"[L6-C] CircuitBreaker open result.IsFailure: {cbOpenResult.IsFailure}");
        Console.WriteLine($"[L6-C] Error code: {cbOpenResult.Error.Code}");

        // ---------------------------------------------------------------
        // L6-D: TransportAckResult enum — transport-level acknowledgement
        //        Ack        = message processed, remove from queue
        //        NackRequeue = transient failure, put back in queue
        //        DeadLetter  = fatal failure, route to DLQ
        // ---------------------------------------------------------------
        Console.WriteLine($"[L6-D] TransportAckResult.Ack        = {(int)TransportAckResult.Ack}");
        Console.WriteLine($"[L6-D] TransportAckResult.NackRequeue = {(int)TransportAckResult.NackRequeue}");
        Console.WriteLine($"[L6-D] TransportAckResult.DeadLetter  = {(int)TransportAckResult.DeadLetter}");

        // ---------------------------------------------------------------
        // L6-E: IDeadLetterQueue / DeadLetterReason
        //        IDeadLetterQueue is an application-level contract.
        //        No built-in default implementation is provided; applications
        //        implement and register their own DLQ adapter.
        //
        // GAP: No default IDeadLetterQueue implementation ships with the library.
        //      Applications must implement it per their storage strategy.
        // ---------------------------------------------------------------

        // DeadLetterReason factory method
        var dlReason = DeadLetterReason.FromException(
            reasonCode: "MAX_RETRIES_EXCEEDED",
            description: "Handler failed after maximum retry attempts.",
            exception: new InvalidOperationException("simulated failure"));

        Console.WriteLine($"[L6-E] DeadLetterReason.ReasonCode    : {dlReason.ReasonCode}");
        Console.WriteLine($"[L6-E] DeadLetterReason.Description   : {dlReason.Description}");
        Console.WriteLine($"[L6-E] DeadLetterReason.ExceptionType : {dlReason.ExceptionType}");
        Console.WriteLine($"[L6-E] DeadLetterReason.OccurredAtUtc : {dlReason.OccurredAtUtc:O}");

        // DeadLetterReason positional constructor (record)
        var dlReason2 = new DeadLetterReason(
            ReasonCode: "DESERIALIZATION_FAILURE",
            Description: "Payload could not be deserialized.",
            ExceptionType: "System.Text.Json.JsonException",
            StackTrace: null,
            OccurredAtUtc: DateTimeOffset.UtcNow);
        Console.WriteLine($"[L6-E] DeadLetterReason (ctor) ReasonCode: {dlReason2.ReasonCode}");

        // Demonstrate how a DLQ adapter would be called (implementation in L8)
        var customDlq = new InMemoryDeadLetterQueue();
        var dlResult = await customDlq.ForwardToDeadLetterAsync(
            invalidMsg,
            dlReason,
            context: null,
            cancellationToken: CancellationToken.None);
        PrintResult("L6-E IDeadLetterQueue.ForwardToDeadLetterAsync", dlResult);

        // ForwardRawToDeadLetterAsync overload
        var rawPayload = app.Services.GetRequiredService<IMessageSerializer>()
                           .Serialize(invalidMsg);
        var dlResult2 = await customDlq.ForwardRawToDeadLetterAsync(
            rawPayload,
            dlReason,
            metadata: null,
            cancellationToken: CancellationToken.None);
        PrintResult("L6-E IDeadLetterQueue.ForwardRawToDeadLetterAsync", dlResult2);

        await app.StopAsync();
    }

    // =========================================================================
    // L7 — SCALABILITY
    // =========================================================================

    private static async Task RunLevel7_ScalabilityAsync()
    {
        Section("L7 — Scalability");

        // ---------------------------------------------------------------
        // L7-A: TransportSubscriptionOptions — concurrency control
        //        MaxConcurrency: max concurrent handler executions per destination
        //        PrefetchCount:  messages pre-fetched from broker per poll
        //        ConsumerGroup:  broker-level consumer group / competing consumers
        // ---------------------------------------------------------------
        var subOpts = new TransportSubscriptionOptions
        {
            MaxConcurrency = Environment.ProcessorCount * 4,
            PrefetchCount = 50,
            ConsumerGroup = "payment-processors"
        };
        Console.WriteLine($"[L7-A] MaxConcurrency: {subOpts.MaxConcurrency}");
        Console.WriteLine($"[L7-A] PrefetchCount : {subOpts.PrefetchCount}");
        Console.WriteLine($"[L7-A] ConsumerGroup : {subOpts.ConsumerGroup}");

        // ---------------------------------------------------------------
        // L7-B: InMemoryTransportOptions — channel capacity tuning
        //        ChannelCapacity: bounded buffer size
        //        FullMode       : BoundedChannelFullMode (Wait, DropNewest, etc.)
        // ---------------------------------------------------------------
        var inMemOpts = new InMemoryTransportOptions
        {
            ChannelCapacity = 50_000,
            FullMode = BoundedChannelFullMode.Wait
        };
        Console.WriteLine($"[L7-B] InMemoryTransportOptions.ChannelCapacity: {inMemOpts.ChannelCapacity}");
        Console.WriteLine($"[L7-B] InMemoryTransportOptions.FullMode        : {inMemOpts.FullMode}");

        // ---------------------------------------------------------------
        // L7-C: IBatchMessageTransport — optimised batch publishing
        //        When the active transport implements IBatchMessageTransport,
        //        PublishBatchAsync and SendBatchAsync use PublishBatchRawAsync
        //        for a single broker round-trip per batch.
        //
        // IBatchMessageTransport.PublishBatchRawAsync signature:
        //   ValueTask<Result> PublishBatchRawAsync(
        //       string destination,
        //       IReadOnlyList<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)> batch,
        //       CancellationToken ct)
        //
        // InMemoryMessageTransport implements BOTH IBatchMessageTransport AND IDeferableMessageTransport.
        // Custom broker transports (Kafka, RabbitMQ, Azure SB, AWS SQS) implement the relevant
        // interfaces based on what the broker natively supports.
        // ---------------------------------------------------------------
        var batchTransport = new InMemoryMessageTransport();
        Console.WriteLine($"[L7-C] InMemoryMessageTransport implements IBatchMessageTransport : {batchTransport is IBatchMessageTransport}");
        Console.WriteLine($"[L7-C] InMemoryMessageTransport implements IDeferableMessageTransport: {batchTransport is IDeferableMessageTransport}");
        Console.WriteLine("       See L8-C for a custom IBatchMessageTransport implementation.");
        await batchTransport.DisposeAsync();

        // ---------------------------------------------------------------
        // L7-D: IDeferableMessageTransport — scheduled delivery
        //        DeferRawAsync delivers a message after a configured delay.
        //        InMemoryMessageTransport implements this via Task.Delay + TimeProvider.
        //        AzureServiceBusMessageTransport implements it via ScheduleMessageAsync.
        //
        // IDeferableMessageTransport.DeferRawAsync signature:
        //   ValueTask<Result> DeferRawAsync(
        //       string destination,
        //       ReadOnlyMemory<byte> payload,
        //       TransportMessageMetadata metadata,
        //       TimeSpan delay,
        //       CancellationToken ct)
        // ---------------------------------------------------------------
        var deferTransport = new InMemoryMessageTransport();
        Console.WriteLine($"[L7-D] InMemoryMessageTransport implements IDeferableMessageTransport: {deferTransport is IDeferableMessageTransport}");

        // Demonstrate DeferRawAsync directly on IDeferableMessageTransport
        if (deferTransport is IDeferableMessageTransport deferable)
        {
            var deferSerializer = new NativeAotJsonSerializer(ShowcaseJsonContext.Default);
            var deferPayload = deferSerializer.Serialize(new PingMessage("deferred-ping"));
            var deferMeta = TransportMessageMetadata.Create("showcase.ping.v1");

            // Delay of zero immediately publishes; positive delays use Task.Delay + TimeProvider
            var deferResult = await deferable.DeferRawAsync(
                "showcase.ping.v1",
                deferPayload,
                deferMeta,
                delay: TimeSpan.Zero, // immediate for demo; replace with TimeSpan.FromSeconds(5) for real scheduling
                cancellationToken: CancellationToken.None);
            PrintResult("[L7-D] IDeferableMessageTransport.DeferRawAsync (delay=0)", deferResult);
        }

        await deferTransport.DisposeAsync();

        // ---------------------------------------------------------------
        // L7-E: MessagingDiagnostics metrics
        //        Instrument members accessed by name for reference completeness.
        //        Counters are instrumented automatically by MessagePublisher and
        //        MessageConsumer. Use in production with OTel Collector or Prometheus.
        // ---------------------------------------------------------------
        Console.WriteLine($"[L7-E] ActivitySource name : {MessagingDiagnostics.ActivitySourceName}");
        Console.WriteLine($"[L7-E] Meter name          : {MessagingDiagnostics.MeterName}");
        Console.WriteLine($"[L7-E] Version             : {MessagingDiagnostics.Version}");
        Console.WriteLine($"[L7-E] ActivitySource      : {MessagingDiagnostics.ActivitySource.Name}");
        Console.WriteLine($"[L7-E] Meter               : {MessagingDiagnostics.Meter.Name}");
        // Instrument references — Counter<long> and Histogram<double>:
        Console.WriteLine($"[L7-E] MessagesPublished     : {MessagingDiagnostics.MessagesPublished.Name}");
        Console.WriteLine($"[L7-E] MessagesReceived      : {MessagingDiagnostics.MessagesReceived.Name}");
        Console.WriteLine($"[L7-E] MessagesFailed        : {MessagingDiagnostics.MessagesFailed.Name}");
        Console.WriteLine($"[L7-E] MessagesDeduplicated  : {MessagingDiagnostics.MessagesDeduplicated.Name}");
        Console.WriteLine($"[L7-E] ProcessingDuration    : {MessagingDiagnostics.ProcessingDuration.Name}");

        await Task.CompletedTask;
    }

    // =========================================================================
    // L8 — CUSTOMISATION
    // =========================================================================

    private static async Task RunLevel8_CustomisationAsync()
    {
        Section("L8 — Customisation");

        // ---------------------------------------------------------------
        // L8-A: Custom IMessageMiddleware implementation
        //        AuditMiddleware is registered via:
        //          opts.AddMiddleware<AuditMiddleware>()
        //        IMessageMiddleware.InvokeAsync signature:
        //          ValueTask<Result> InvokeAsync(
        //              MessageContext context,
        //              MessageExecutionDelegate next,
        //              CancellationToken cancellationToken)
        //
        //        MessageExecutionDelegate:
        //          delegate ValueTask<Result> MessageExecutionDelegate(
        //              MessageContext context, CancellationToken ct)
        // ---------------------------------------------------------------
        Console.WriteLine("[L8-A] Custom IMessageMiddleware: see AuditMiddleware below.");

        // ---------------------------------------------------------------
        // L8-B: MiddlewarePipeline — manually build and execute a pipeline
        //        Useful in unit tests and advanced integration scenarios.
        // ---------------------------------------------------------------
        var pipeline = new MiddlewarePipeline(new IMessageMiddleware[]
        {
            new AuditMiddleware(),
            new RetryMiddleware(maxRetries: 2, initialDelay: TimeSpan.FromMilliseconds(10))
        });

        var app = BuildMinimalHost(s => s.AddMessageHandler<PingMessage, PingHandler>());
        await app.StartAsync();

        var pipelineCtx = new MessageContext(
            TransportMessageMetadata.Create("ping.v1"),
            app.Services,
            CancellationToken.None)
        { Message = new PingMessage("pipeline-test") };

        var pipelineResult = await pipeline.ExecuteAsync(
            pipelineCtx,
            (_, _) => ValueTask.FromResult(Result.Success()));
        PrintResult("L8-B MiddlewarePipeline.ExecuteAsync", pipelineResult);

        // ---------------------------------------------------------------
        // L8-C: Custom IMessageTransport implementation
        //        CustomBatchTransport implements both IMessageTransport
        //        and IBatchMessageTransport to demonstrate full extensibility.
        // ---------------------------------------------------------------
        Console.WriteLine("[L8-C] Custom IMessageTransport: see CustomBatchTransport below.");

        var customTransport = new CustomBatchTransport();

        // Use IMessageTransport.PublishRawAsync
        var rawMeta = TransportMessageMetadata.Create("ping.v1");
        var rawPayload = new byte[] { 0x7B, 0x7D }; // {}
        var rawResult = await customTransport.PublishRawAsync(
            "custom.destination",
            rawPayload,
            rawMeta);
        PrintResult("L8-C IMessageTransport.PublishRawAsync", rawResult);

        // Use IBatchMessageTransport.PublishBatchRawAsync
        var batch = new List<(ReadOnlyMemory<byte>, TransportMessageMetadata)>
        {
            (rawPayload, TransportMessageMetadata.Create("ping.v1")),
            (rawPayload, TransportMessageMetadata.Create("ping.v1")),
        };
        var batchResult = await customTransport.PublishBatchRawAsync("custom.destination", batch);
        PrintResult("L8-C IBatchMessageTransport.PublishBatchRawAsync", batchResult);

        await customTransport.DisposeAsync();

        // ---------------------------------------------------------------
        // L8-D: Custom IMessageSerializer implementation
        //        NativeAotJsonSerializer constructors:
        //          NativeAotJsonSerializer()                              — default
        //          NativeAotJsonSerializer(IJsonTypeInfoResolver)         — resolver
        //          NativeAotJsonSerializer(IJsonTypeInfoResolver?, opts?) — resolver + IOptions<> (DI)
        //          NativeAotJsonSerializer(JsonSerializerOptions?)        — options only
        //
        //        IMessageSerializer members:
        //          string ContentType { get; }
        //          ReadOnlyMemory<byte> Serialize<T>(T message)
        //          void Serialize<T>(T message, IBufferWriter<byte> writer)
        //          T Deserialize<T>(ReadOnlyMemory<byte> bytes)
        //          object Deserialize(ReadOnlyMemory<byte> bytes, Type messageType)
        // ---------------------------------------------------------------

        // Constructor overload 1: default (NativeAotJsonSerializer())
        IMessageSerializer serializer1 = new NativeAotJsonSerializer();
        Console.WriteLine($"[L8-D] NativeAotJsonSerializer() ContentType: {serializer1.ContentType}");

        // Constructor overload 2: NativeAotJsonSerializer(IJsonTypeInfoResolver)
        //   Use when you need a resolver without overriding the full JsonSerializerOptions.
        IMessageSerializer serializer2a = new NativeAotJsonSerializer((IJsonTypeInfoResolver)ShowcaseJsonContext.Default);
        Console.WriteLine($"[L8-D] NativeAotJsonSerializer(IJsonTypeInfoResolver) ContentType: {serializer2a.ContentType}");

        // Constructor overload 3: NativeAotJsonSerializer(IJsonTypeInfoResolver?, IOptions<JsonSerializerOptions>?)
        //   The DI-friendly overload. IOptions<JsonSerializerOptions> takes precedence when set.
        //   Simulated here with Options.Create:
        var diJsonOptions = Microsoft.Extensions.Options.Options.Create(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = ShowcaseJsonContext.Default
        });
        IMessageSerializer serializer2b = new NativeAotJsonSerializer(ShowcaseJsonContext.Default, diJsonOptions);
        Console.WriteLine($"[L8-D] NativeAotJsonSerializer(IJsonTypeInfoResolver?, IOptions<>?) ContentType: {serializer2b.ContentType}");

        // Constructor overload 4: NativeAotJsonSerializer(JsonSerializerOptions?)
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                ShowcaseJsonContext.Default,
                MessagingJsonContext.Default)
        };
        IMessageSerializer serializer2 = new NativeAotJsonSerializer(jsonOptions);

        // Serialize<T> — returns ReadOnlyMemory<byte>
        var ping = new PingMessage("serializer-test");
        var bytes = serializer2.Serialize(ping);
        Console.WriteLine($"[L8-D] Serialize<PingMessage> byte count: {bytes.Length}");

        // Serialize<T> with IBufferWriter<byte>
        var buffer = new ArrayBufferWriter<byte>();
        serializer2.Serialize(ping, buffer);
        Console.WriteLine($"[L8-D] Serialize<PingMessage> (IBufferWriter) byte count: {buffer.WrittenCount}");

        // Deserialize<T>
        var deserialized = serializer2.Deserialize<PingMessage>(bytes);
        Console.WriteLine($"[L8-D] Deserialize<PingMessage>.Payload: {deserialized.Payload}");

        // Deserialize(bytes, Type)
        var deserializedObj = serializer2.Deserialize(bytes, typeof(PingMessage));
        Console.WriteLine($"[L8-D] Deserialize(bytes, Type) type: {deserializedObj.GetType().Name}");

        // MessagingJsonContext (source-generated, for AOT)
        Console.WriteLine($"[L8-D] MessagingJsonContext.Default is not null: {MessagingJsonContext.Default is not null}");

        // Custom serializer implementation — see CustomXmlSerializer class below
        Console.WriteLine("[L8-D] Custom IMessageSerializer: see CustomXmlSerializer below.");

        // ---------------------------------------------------------------
        // L8-E: DefaultMessageDispatcher.RegisterHandler<TMessage, THandler>
        //        Allows imperative handler binding at runtime (rare; prefer DI registration).
        // ---------------------------------------------------------------
        // (DefaultMessageDispatcher is a singleton exposed through IMessageDispatcher;
        //  RegisterHandler is called by the HandlerRegistration pipeline automatically.)
        // This demonstrates that DefaultMessageDispatcher is the concrete type.
        var dispatcher = app.Services.GetRequiredService<IMessageDispatcher>();
        Console.WriteLine($"[L8-E] IMessageDispatcher concrete type: {dispatcher.GetType().Name}");

        // ---------------------------------------------------------------
        // L8-F: IHandlerRegistration / HandlerRegistrationBase
        //        The framework provides HandlerRegistrationBase and IHandlerRegistration
        //        for advanced scenarios where handler bindings are created programmatically.
        // ---------------------------------------------------------------
        Console.WriteLine("[L8-F] IHandlerRegistration.TypeName is used by AddMessageHandler<T,H> internally.");
        Console.WriteLine("       HandlerRegistrationBase provides shared TypeName storage.");

        // ---------------------------------------------------------------
        // L8-G: IMessageUpcasterInvoker / MessageUpcasterInvoker<TOld,TNew,TUpcaster>
        //        These are internal to the upcasting pipeline.
        //        IMessageUpcasterInvoker.SourceType exposes the source CLR type.
        //        MessageUpcasterInvoker<> bridges typed upcaster to runtime dispatch.
        // ---------------------------------------------------------------
        Console.WriteLine("[L8-G] IMessageUpcasterInvoker.SourceType returns the old message CLR type.");
        Console.WriteLine("       MessageUpcasterInvoker<TOld,TNew,TUpcaster> adapts typed upcasters.");

        await app.StopAsync();
    }

    // =========================================================================
    // L9 — BROKER EXTENSIONS (configuration only — no broker running)
    // =========================================================================

    private static void RunLevel9_BrokerExtensionConfiguration()
    {
        Section("L9 — Broker Extensions (Configuration)");

        // ---------------------------------------------------------------
        // L9-A: RabbitMQ transport
        //        AddRabbitMqMessagingTransport replaces IMessageTransport with
        //        RabbitMqMessageTransport. Options: HostName, Port, VirtualHost,
        //        UserName, Password, ExchangeName.
        // ---------------------------------------------------------------
        Console.WriteLine("[L9-A] RabbitMQ transport configuration:");
        Console.WriteLine("       services.AddRabbitMqMessagingTransport(opts => {");
        Console.WriteLine("           opts.HostName     = \"rabbitmq.cluster.local\";");
        Console.WriteLine("           opts.Port         = 5672;");
        Console.WriteLine("           opts.VirtualHost  = \"/production\";");
        Console.WriteLine("           opts.UserName     = \"app-user\";");
        Console.WriteLine("           opts.Password     = \"<secret>\";");
        Console.WriteLine("           opts.ExchangeName = \"messaging.exchange\";");
        Console.WriteLine("       });");

        // RabbitMqTransportOptions defaults
        var rabbitOpts = new EricksonLopez.Messaging.Transport.RabbitMQ.RabbitMqTransportOptions();
        Console.WriteLine($"[L9-A] RabbitMqTransportOptions.HostName     default: {rabbitOpts.HostName}");
        Console.WriteLine($"[L9-A] RabbitMqTransportOptions.Port         default: {rabbitOpts.Port}");
        Console.WriteLine($"[L9-A] RabbitMqTransportOptions.VirtualHost  default: {rabbitOpts.VirtualHost}");
        Console.WriteLine($"[L9-A] RabbitMqTransportOptions.ExchangeName default: '{rabbitOpts.ExchangeName}'");

        // ---------------------------------------------------------------
        // L9-B: Apache Kafka transport
        //        AddKafkaMessagingTransport registers KafkaMessageTransport.
        //        Options: BootstrapServers, GroupId, ClientId, EnableAutoCommit.
        // ---------------------------------------------------------------
        Console.WriteLine("[L9-B] Kafka transport configuration:");
        Console.WriteLine("       services.AddKafkaMessagingTransport(opts => {");
        Console.WriteLine("           opts.BootstrapServers  = \"kafka-broker-1:9092,kafka-broker-2:9092\";");
        Console.WriteLine("           opts.GroupId           = \"order-processing-group\";");
        Console.WriteLine("           opts.ClientId          = \"showcase-producer\";");
        Console.WriteLine("           opts.EnableAutoCommit  = false;");
        Console.WriteLine("       });");

        var kafkaOpts = new EricksonLopez.Messaging.Transport.Kafka.KafkaTransportOptions();
        Console.WriteLine($"[L9-B] KafkaTransportOptions.BootstrapServers default: {kafkaOpts.BootstrapServers}");
        Console.WriteLine($"[L9-B] KafkaTransportOptions.GroupId           default: {kafkaOpts.GroupId}");
        Console.WriteLine($"[L9-B] KafkaTransportOptions.EnableAutoCommit  default: {kafkaOpts.EnableAutoCommit}");

        // ---------------------------------------------------------------
        // L9-C: Azure Service Bus transport
        //        AddAzureServiceBusMessagingTransport registers AzureServiceBusMessageTransport.
        //        Options: ConnectionString, FullyQualifiedNamespace, Credential (TokenCredential)
        // ---------------------------------------------------------------
        Console.WriteLine("[L9-C] Azure Service Bus transport configuration:");
        Console.WriteLine("       services.AddAzureServiceBusMessagingTransport(opts => {");
        Console.WriteLine("           opts.ConnectionString = \"Endpoint=sb://...;...\";");
        Console.WriteLine("           // OR for managed identity:");
        Console.WriteLine("           opts.FullyQualifiedNamespace = \"ns.servicebus.windows.net\";");
        Console.WriteLine("           opts.Credential = new DefaultAzureCredential();");
        Console.WriteLine("       });");

        var asbOpts = new EricksonLopez.Messaging.Transport.AzureServiceBus.AzureServiceBusTransportOptions();
        Console.WriteLine($"[L9-C] AzureServiceBusTransportOptions.ConnectionString default: '{asbOpts.ConnectionString}'");
        Console.WriteLine($"[L9-C] AzureServiceBusTransportOptions.FullyQualifiedNamespace default: '{asbOpts.FullyQualifiedNamespace}'");
        // AzureServiceBusTransportOptions.Credential is of type Azure.Core.TokenCredential.
        // Accessing it at runtime requires Azure.Core, which is a transitive dependency.
        // Set it in production: opts.Credential = new DefaultAzureCredential();
        Console.WriteLine("[L9-C] AzureServiceBusTransportOptions.Credential — set to a TokenCredential for Managed Identity auth.");

        // ---------------------------------------------------------------
        // L9-D: AWS SQS transport
        //        AddAwsSqsMessagingTransport registers AwsSqsMessageTransport.
        //        Options: Region, ServiceUrl, WaitTimeSeconds, MaxNumberOfMessages
        // ---------------------------------------------------------------
        Console.WriteLine("[L9-D] AWS SQS transport configuration:");
        Console.WriteLine("       services.AddAwsSqsMessagingTransport(opts => {");
        Console.WriteLine("           opts.Region             = \"us-east-1\";");
        Console.WriteLine("           opts.ServiceUrl         = null; // or LocalStack endpoint");
        Console.WriteLine("           opts.WaitTimeSeconds    = 20;");
        Console.WriteLine("           opts.MaxNumberOfMessages= 10;");
        Console.WriteLine("       });");

        var sqsOpts = new EricksonLopez.Messaging.Transport.AwsSqs.AwsSqsTransportOptions();
        Console.WriteLine($"[L9-D] AwsSqsTransportOptions.Region              default: {sqsOpts.Region}");
        Console.WriteLine($"[L9-D] AwsSqsTransportOptions.WaitTimeSeconds     default: {sqsOpts.WaitTimeSeconds}");
        Console.WriteLine($"[L9-D] AwsSqsTransportOptions.MaxNumberOfMessages default: {sqsOpts.MaxNumberOfMessages}");

        // ---------------------------------------------------------------
        // L9-E: MessagingEventPublisher (EricksonLopez.Messaging.Events)
        //        Bridges IEventPublisher (from EricksonLopez.Events.Contracts)
        //        to IMessagePublisher.
        //
        //   services.AddMessaging();
        //   services.AddMessagingEventPublisher(opts => {
        //       opts.ThrowOnFailure     = true;
        //       opts.DestinationResolver = t => $"{t.Name.ToLower()}.v1";
        //   });
        Console.WriteLine("[L9-E] MessagingEventsOptions.ThrowOnFailure (default: true)");
        Console.WriteLine("[L9-E] MessagingEventsOptions.DestinationResolver (default: null → uses [MessageType])");

        // Execute DI registrations to validate all transport and event publisher extensions
        var brokerServices = new ServiceCollection();
        brokerServices.AddRabbitMqMessagingTransport(opts => {
            opts.HostName = rabbitOpts.HostName;
        });
        brokerServices.AddKafkaMessagingTransport(opts => {
            opts.BootstrapServers = kafkaOpts.BootstrapServers;
        });
        brokerServices.AddAzureServiceBusMessagingTransport(opts => {
            opts.ConnectionString = "Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=mock=";
        });
        brokerServices.AddAwsSqsMessagingTransport(opts => {
            opts.Region = sqsOpts.Region;
        });
        brokerServices.AddMessagingEventPublisher(opts => {
            opts.ThrowOnFailure = true;
        });
        Console.WriteLine("       [SUCCESS] All 5 broker & event publisher extensions registered via DI successfully.");
    }

    // =========================================================================
    // L10 — ENTERPRISE PATTERNS
    // =========================================================================

    private static async Task RunLevel10_EnterpriseAsync()
    {
        Section("L10 — Enterprise Patterns");

        // ---------------------------------------------------------------
        // L10-A: OpenTelemetry distributed tracing & metrics
        //        AddMessagingInstrumentation() — TracerProviderBuilder extension
        //        AddMessagingInstrumentation() — MeterProviderBuilder extension
        //        MessagingDiagnostics.ActivitySourceName = "EricksonLopez.Messaging"
        //        MessagingDiagnostics.MeterName          = "EricksonLopez.Messaging"
        // ---------------------------------------------------------------
        Console.WriteLine("[L10-A] OpenTelemetry setup (Tracing + Metrics):");
        Console.WriteLine("        builder.Services.AddOpenTelemetry()");
        Console.WriteLine("            .WithTracing(t => {");
        Console.WriteLine("                t.AddMessagingInstrumentation();");
        Console.WriteLine("                t.AddConsoleExporter();");
        Console.WriteLine("            })");
        Console.WriteLine("            .WithMetrics(m => {");
        Console.WriteLine("                m.AddMessagingInstrumentation();");
        Console.WriteLine("                m.AddConsoleExporter();");
        Console.WriteLine("            });");

        // ---------------------------------------------------------------
        // L10-B: Health Checks
        //        MessagingHealthCheck — IHealthCheck implementation
        //        AddMessagingHealthCheck() — IServiceCollection extension
        //
        //   builder.Services.AddMessagingHealthCheck();
        //   // Exposed via ASP.NET Core health check endpoint: /health
        // ---------------------------------------------------------------
        var healthCheck = new MessagingHealthCheck(publisher: null);
        var healthDegraded = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        Console.WriteLine($"[L10-B] MessagingHealthCheck (no publisher): {healthDegraded.Status}");

        // With a publisher provided (requires a real service scope)
        var app = BuildMinimalHost();
        await app.StartAsync();
        var pub = app.Services.GetRequiredService<IMessagePublisher>();
        var healthOk = new MessagingHealthCheck(pub);
        var healthOkResult = await healthOk.CheckHealthAsync(new HealthCheckContext());
        Console.WriteLine($"[L10-B] MessagingHealthCheck (with publisher): {healthOkResult.Status}");

        // ---------------------------------------------------------------
        // L10-C: Message Schema Evolution with Upcasting
        //        IMessageUpcaster<TOldMessage, TNewMessage>
        //          TOldMessage : class, IMessage
        //          TNewMessage : class, IMessage
        //        Upcast(TOldMessage oldMessage, TransportMessageMetadata metadata)
        //
        //        AddMessageUpcaster<TOld, TNew, TUpcaster>() — DI registration
        //        AddUpcasting()                               — pipeline middleware
        // ---------------------------------------------------------------
        Console.WriteLine("[L10-C] Schema evolution:");
        Console.WriteLine("        [MessageType(\"orders.placed.v1\")] OrderPlacedEventV1 → V2");
        Console.WriteLine("        IMessageUpcaster<V1, V2>.Upcast(v1, metadata) → V2");
        Console.WriteLine("        AddMessageUpcaster<V1, V2, OrderPlacedUpcaster>()");
        Console.WriteLine("        AddUpcasting() activates MessageUpcastingMiddleware.");

        var upcaster = new OrderPlacedUpcaster();
        var v1Message = new OrderPlacedEventV1(Guid.NewGuid(), "ORD-LEGACY-001", 299.00m);
        var v2Message = upcaster.Upcast(
            v1Message,
            TransportMessageMetadata.Create("orders.placed.v1"));

        Console.WriteLine($"[L10-C] Upcasted: {v1Message.OrderNumber} → V2 Currency={v2Message.Currency}, Channel={v2Message.Channel}");

        // ---------------------------------------------------------------
        // L10-D: Partition Key Attribute & IPartitionKeyResolver
        //        [PartitionKey] decorates a property in an IMessage record.
        //        IPartitionKeyResolver extracts partition key at runtime without reflection.
        // ---------------------------------------------------------------
        Console.WriteLine("[L10-D] [PartitionKey] attribute marks a property for partitioned routing.");
        Console.WriteLine("        See PartitionedOrderMessage below.");
        IPartitionKeyResolver partitionResolver = new CustomOrderPartitionKeyResolver();
        var partitionedMsg = new PartitionedOrderMessage(Guid.NewGuid(), "EU-CENTRAL");
        var resolvedKey = partitionResolver.Resolve(partitionedMsg);
        Console.WriteLine($"[L10-D] IPartitionKeyResolver resolved: key='{resolvedKey}' for order {partitionedMsg.OrderId}");

        // ---------------------------------------------------------------
        // L10-E: InMemoryTestHarness — unit testing integration
        //        PublishedMessages  : PublishedMessageList (IReadOnlyList<PublishedMessage>)
        //        ConsumedMessages   : ConsumedMessageList (IReadOnlyList<ConsumedMessage>)
        //        WaitUntilPublishedAsync(messageType, timeout, ct)
        //        PublishedMessageList.Contains(messageType)
        //        PublishedMessageList.OfType(messageType)
        //        ConsumedMessageList.Contains(messageType)
        //        ConsumedMessageList.OfType(messageType)
        //        ConsumedMessageList.AnySucceeded(messageType)
        //        PublishedMessage: Destination, Payload, Metadata (record)
        //        ConsumedMessage : Destination, Payload, Metadata, Succeeded (record)
        // ---------------------------------------------------------------
        var harness = new InMemoryTestHarness();
        var testMeta = TransportMessageMetadata.Create("ping.v1");
        var testPayload = new byte[] { 0x7B, 0x7D };

        await harness.PublishRawAsync("ping.v1", testPayload, testMeta);
        Console.WriteLine($"[L10-E] Harness published count     : {harness.PublishedMessages.Count}");
        Console.WriteLine($"[L10-E] Harness.Contains('ping.v1'): {harness.PublishedMessages.Contains("ping.v1")}");

        foreach (var msg in harness.PublishedMessages.OfType("ping.v1"))
        {
            var (pDest, pPayload, pMeta) = msg; // Deconstruct
            Console.WriteLine($"[L10-E] PublishedMessage: Destination={pDest}, Payload.Length={pPayload.Length}, Type={pMeta.MessageType}");
        }

        // WaitUntilPublishedAsync — async wait for message arrival in tests
        var arrived = await harness.WaitUntilPublishedAsync(
            "ping.v1", timeout: TimeSpan.FromMilliseconds(100));
        Console.WriteLine($"[L10-E] WaitUntilPublishedAsync ('ping.v1'): {arrived}");

        // Subscribe to consume the messages synchronously in the harness
        var subOptions = new TransportSubscriptionOptions();
        await harness.SubscribeAsync(
            "ping.v1",
            (payload, meta, ct) => ValueTask.FromResult(TransportAckResult.Ack),
            subOptions);
        Console.WriteLine($"[L10-E] Consumed count      : {harness.ConsumedMessages.Count}");
        Console.WriteLine($"[L10-E] AnySucceeded('ping.v1'): {harness.ConsumedMessages.AnySucceeded("ping.v1")}");

        foreach (var msg in harness.ConsumedMessages.OfType("ping.v1"))
        {
            var (cDest, cPayload, cMeta, cSucc) = msg; // Deconstruct
            Console.WriteLine($"[L10-E] ConsumedMessage: Destination={cDest}, Succeeded={cSucc}, Payload.Length={cPayload.Length}");
        }

        // WaitUntilConsumedAsync — async wait for consumption in integration tests
        // (message already consumed above; should return true immediately)
        var consumed = await harness.WaitUntilConsumedAsync(
            "ping.v1", timeout: TimeSpan.FromMilliseconds(100));
        Console.WriteLine($"[L10-E] WaitUntilConsumedAsync('ping.v1'): {consumed}");

        // IAsyncDisposable
        await harness.DisposeAsync();

        await app.StopAsync();
    }

    // =========================================================================
    // L11 — ADVANCED DISPATCH & TRANSPORT INTERNALS
    // =========================================================================

    private static async Task RunLevel11_AdvancedDispatchAndTransportInternalsAsync()
    {
        Section("L11 — Advanced Dispatch, Pipeline & Transport Internals");

        // 1. IDeferableMessageTransport & DeferRawAsync
        Console.WriteLine("[L11-A] DeferRawAsync demonstration on InMemoryMessageTransport:");
        var transport = new InMemoryMessageTransport();
        var metadata = TransportMessageMetadata.Create("orders.delayed");
        var deferResult = await transport.DeferRawAsync("orders.delayed", new byte[] { 1, 2, 3 }, metadata, TimeSpan.FromMilliseconds(50));
        Console.WriteLine($"       DeferRawAsync executed: IsSuccess={deferResult.IsSuccess}");

        // 2. IMessageDeduplicationStore — TryAcquireAsync and ReleaseAsync
        Console.WriteLine("[L11-B] IMessageDeduplicationStore (TryAcquireAsync and ReleaseAsync):");
        IMessageDeduplicationStore dedupStore = new InMemoryMessageDeduplicationStore();
        var acquired1 = await dedupStore.TryAcquireAsync("msg-001", TimeSpan.FromMinutes(1));
        var acquired2 = await dedupStore.TryAcquireAsync("msg-001", TimeSpan.FromMinutes(1));
        Console.WriteLine($"       Acquire 1: {acquired1} (expected True), Acquire 2: {acquired2} (expected False)");
        await dedupStore.ReleaseAsync("msg-001");
        var acquired3 = await dedupStore.TryAcquireAsync("msg-001", TimeSpan.FromMinutes(1));
        Console.WriteLine($"       After ReleaseAsync -> Acquire 3: {acquired3} (expected True)");

        // 3. MiddlewarePipeline — BuildChain
        Console.WriteLine("[L11-C] MiddlewarePipeline.BuildChain demonstration:");
        var pipeline = new MiddlewarePipeline();
        var chain = pipeline.BuildChain((ctx, ct) => ValueTask.FromResult(Result.Success()));
        var mockContext = new MessageContext(
            metadata,
            new ServiceCollection().BuildServiceProvider(),
            CancellationToken.None);
        var chainResult = await chain(mockContext, CancellationToken.None);
        Console.WriteLine($"       BuildChain executed: IsSuccess={chainResult.IsSuccess}");

        // 4. Dispatcher, IHandlerRegistry, and HandlerRegistrationBase
        Console.WriteLine("[L11-D] Dispatcher handler registration and batch dispatch:");
        var serializer = new NativeAotJsonSerializer();
        var dispatcher = new DefaultMessageDispatcher(serializer);

        // RegisterHandler on DefaultMessageDispatcher
        dispatcher.RegisterHandler<PingMessage, PingHandler>("sample.ping.direct");

        // RegisterHandler on IHandlerRegistry
        IHandlerRegistry registry = dispatcher;
        registry.RegisterHandler<PingMessage, PingHandler>("sample.ping.registry");

        // IHandlerRegistration.Register
        IHandlerRegistration registration = new ShowcaseHandlerRegistration("sample.ping.reg");
        registration.Register(dispatcher);
        registration.Register(registry);

        // DispatchBatchAsync
        var batchItems = new List<MessageDispatchItem>
        {
            new("sample.ping.direct", JsonSerializer.SerializeToUtf8Bytes(new PingMessage("Batch 1"), ShowcaseJsonContext.Default.PingMessage), metadata)
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<PingHandler>();
        using var provider = services.BuildServiceProvider();

        var batchResult = await dispatcher.DispatchBatchAsync(batchItems, provider, CancellationToken.None);
        Console.WriteLine($"       DispatchBatchAsync executed: IsSuccess={batchResult.IsSuccess}");

        // 5. DefaultMessageDispatcher.HandlerBinding representation
        var sampleBinding = new DefaultMessageDispatcher.HandlerBinding(
            typeof(PingMessage),
            typeof(PingHandler),
            (sp, msg, ctx, ct) => ((PingHandler)sp.GetRequiredService<PingHandler>()).HandleAsync((PingMessage)msg, ctx, ct));
        Console.WriteLine($"[L11-E] HandlerBinding instantiated: Message={sampleBinding.MessageType.Name}, Handler={sampleBinding.HandlerType.Name}");

        Console.WriteLine("       [SUCCESS] All internal dispatch and transport APIs validated.");
    }

    // =========================================================================
    // HELPER METHODS
    // =========================================================================

    private static void PrintBanner()
    {
        Console.WriteLine("========================================================");
        Console.WriteLine(" EricksonLopez.Messaging — Official Reference Showcase");
        Console.WriteLine(" Distributed Messaging & Pub/Sub for .NET 10 (AOT)");
        Console.WriteLine("========================================================");
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {title} ---");
    }

    private static void PrintResult(string operation, Result result)
    {
        if (result.IsSuccess)
            Console.WriteLine($"[{operation}] ✅ Success");
        else
            Console.WriteLine($"[{operation}] ❌ Failure: [{result.Error.Code}] {result.Error.Description}");
    }

    private static IHost BuildMinimalHost(
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMessaging();
        configureServices?.Invoke(builder.Services);
        return builder.Build();
    }
}

// ================================ Extension ================================

internal static class HostApplicationBuilderExtensions
{
    public static HostApplicationBuilder ConfigureServices(this HostApplicationBuilder builder)
    {
        builder.Services.AddMessaging();
        builder.Services.AddMessageHandler<PingMessage, PingHandler>();
        return builder;
    }
}

// =============================================================================
// MESSAGE CONTRACTS — [MessageType] + IMessage
// =============================================================================

/// <summary>
/// Represents a minimal marker message for ping and health verification scenarios.
/// </summary>
/// <param name="Payload">The text payload carried by the ping message.</param>
[MessageType("ping.v1")]
public sealed record PingMessage(string Payload) : IMessage;

/// <summary>
/// Represents an event published when an order is fulfilled.
/// </summary>
/// <param name="OrderId">The unique identifier of the order.</param>
/// <param name="OrderNumber">The human-readable order tracking number.</param>
/// <param name="TotalAmount">The total monetary amount fulfilled.</param>
[MessageType("orders.fulfilled.v1")]
public sealed record OrderFulfilledEvent(
    Guid OrderId,
    string OrderNumber,
    decimal TotalAmount) : IMessage;

/// <summary>
/// Represents a point-to-point command for payment processing.
/// </summary>
/// <param name="PaymentId">The unique identifier of the payment transaction.</param>
/// <param name="OrderNumber">The associated order tracking number.</param>
/// <param name="Amount">The monetary amount to charge.</param>
/// <param name="Currency">The ISO currency code.</param>
[MessageType("payments.process.v1")]
public sealed record ProcessPaymentCommand(
    Guid PaymentId,
    string OrderNumber,
    decimal Amount,
    string Currency) : IMessage;

/// <summary>
/// Represents an inventory item change event transmitted in batches.
/// </summary>
/// <param name="Sku">The stock keeping unit identifier.</param>
/// <param name="QuantityAvailable">The updated available inventory quantity.</param>
/// <param name="UnitPrice">The unit price of the inventory item.</param>
[MessageType("inventory.updated.v1")]
public sealed record InventoryItemUpdatedEvent(
    string Sku,
    int QuantityAvailable,
    decimal UnitPrice) : IMessage;

/// <summary>
/// Represents a notification message sent to external channels.
/// </summary>
/// <param name="NotificationId">The unique identifier of the notification.</param>
/// <param name="Body">The text body content of the notification.</param>
[MessageType("notifications.v1")]
public sealed record NotificationMessage(Guid NotificationId, string Body) : IMessage;

/// <summary>
/// Represents a work item command dispatched in batch operations.
/// </summary>
/// <param name="ItemId">The unique item identifier.</param>
/// <param name="Operation">The operation code to execute.</param>
[MessageType("batch.items.v1")]
public sealed record BatchItemMessage(Guid ItemId, string Operation) : IMessage;

/// <summary>
/// Represents a background scheduled report generation message.
/// </summary>
/// <param name="ReportId">The unique identifier of the scheduled report.</param>
/// <param name="ReportName">The descriptive name of the report.</param>
/// <param name="ScheduledAt">The timestamp at which report generation was requested.</param>
[MessageType("reports.scheduled.v1")]
public sealed record ScheduledReportMessage(
    Guid ReportId,
    string ReportName,
    DateTimeOffset ScheduledAt) : IMessage;

/// <summary>
/// Represents a customer validation request message demonstrating functional failure handling.
/// </summary>
/// <param name="CustomerId">The unique identifier of the customer to validate.</param>
/// <param name="Email">The email address of the customer.</param>
[MessageType("customers.validate.v1")]
public sealed record ValidateCustomerMessage(Guid CustomerId, string Email) : IMessage;

/// <summary>
/// Represents the legacy version 1 schema of an order placed event.
/// </summary>
/// <param name="OrderId">The unique identifier of the order.</param>
/// <param name="OrderNumber">The order tracking number.</param>
/// <param name="Amount">The total order amount.</param>
[MessageType("orders.placed.v1")]
public sealed record OrderPlacedEventV1(
    Guid OrderId,
    string OrderNumber,
    decimal Amount) : IMessage;

/// <summary>
/// Represents the upgraded version 2 schema of an order placed event.
/// </summary>
/// <param name="OrderId">The unique identifier of the order.</param>
/// <param name="OrderNumber">The order tracking number.</param>
/// <param name="Amount">The total order amount.</param>
/// <param name="Currency">The ISO currency code.</param>
/// <param name="Channel">The origination sales channel.</param>
[MessageType("orders.placed.v2")]
public sealed record OrderPlacedEventV2(
    Guid OrderId,
    string OrderNumber,
    decimal Amount,
    string Currency,
    string Channel) : IMessage;

/// <summary>
/// Represents an order message partitioned by geographical region.
/// </summary>
/// <param name="OrderId">The unique identifier of the order.</param>
/// <param name="Region">The geographical region serving as the partition routing key.</param>
[MessageType("orders.partitioned.v1")]
public sealed record PartitionedOrderMessage(Guid OrderId, [property: PartitionKey] string Region) : IMessage;

// =============================================================================
// MESSAGE HANDLERS — IMessageHandler<TMessage>
// =============================================================================

/// <summary>
/// Handles <see cref="PingMessage"/> instances for health check and connectivity testing.
/// </summary>
public sealed class PingHandler : IMessageHandler<PingMessage>
{
    private readonly ILogger<PingHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PingHandler"/> class.
    /// </summary>
    /// <param name="logger">The logger used to record ping events.</param>
    public PingHandler(ILogger<PingHandler> logger) => _logger = logger;

    /// <inheritdoc/>
    public ValueTask<Result> HandleAsync(
        PingMessage message, MessageContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("--> [PingHandler] Payload={Payload} | MessageId={MessageId}",
            message.Payload, context.Metadata.MessageId);
        return ValueTask.FromResult(Result.Success());
    }
}

/// <summary>
/// Demonstrates dynamic message handler registration within a message dispatcher.
/// </summary>
public sealed class ShowcaseHandlerRegistration : HandlerRegistrationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShowcaseHandlerRegistration"/> class.
    /// </summary>
    /// <param name="typeName">The message type discriminator.</param>
    public ShowcaseHandlerRegistration(string typeName) : base(typeName) { }

    /// <inheritdoc/>
    public override void Register(DefaultMessageDispatcher dispatcher)
    {
        dispatcher.RegisterHandler<PingMessage, PingHandler>(TypeName);
    }
}

/// <summary>
/// Handles <see cref="OrderFulfilledEvent"/> messages to record fulfillment status.
/// </summary>
public sealed class OrderFulfilledHandler : IMessageHandler<OrderFulfilledEvent>
{
    private readonly ILogger<OrderFulfilledHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderFulfilledHandler"/> class.
    /// </summary>
    /// <param name="logger">The logger used to record fulfillment events.</param>
    public OrderFulfilledHandler(ILogger<OrderFulfilledHandler> logger) => _logger = logger;

    /// <inheritdoc/>
    public ValueTask<Result> HandleAsync(
        OrderFulfilledEvent message, MessageContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "--> [OrderFulfilledHandler] OrderId={OrderId} {OrderNumber} Total={Total} | MessageId={MessageId}",
            message.OrderId, message.OrderNumber, message.TotalAmount, context.Metadata.MessageId);
        return ValueTask.FromResult(Result.Success());
    }
}

/// <summary>
/// Handles legacy <see cref="OrderPlacedEventV1"/> messages.
/// </summary>
public sealed class OrderPlacedV1Handler : IMessageHandler<OrderPlacedEventV1>
{
    private readonly ILogger<OrderPlacedV1Handler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderPlacedV1Handler"/> class.
    /// </summary>
    /// <param name="logger">The logger used to record order placed events.</param>
    public OrderPlacedV1Handler(ILogger<OrderPlacedV1Handler> logger) => _logger = logger;

    /// <inheritdoc/>
    public ValueTask<Result> HandleAsync(
        OrderPlacedEventV1 message, MessageContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "--> [OrderPlacedV1Handler] {OrderNumber} Amount={Amount}",
            message.OrderNumber, message.Amount);
        return ValueTask.FromResult(Result.Success());
    }
}

/// <summary>
/// Handles version 2 <see cref="OrderPlacedEventV2"/> messages.
/// </summary>
public sealed class OrderPlacedV2Handler : IMessageHandler<OrderPlacedEventV2>
{
    private readonly ILogger<OrderPlacedV2Handler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderPlacedV2Handler"/> class.
    /// </summary>
    /// <param name="logger">The logger used to record order placed events.</param>
    public OrderPlacedV2Handler(ILogger<OrderPlacedV2Handler> logger) => _logger = logger;

    /// <inheritdoc/>
    public ValueTask<Result> HandleAsync(
        OrderPlacedEventV2 message, MessageContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "--> [OrderPlacedV2Handler] {OrderNumber} {Currency} via {Channel} | MessageId={MessageId}",
            message.OrderNumber, message.Currency, message.Channel, context.Metadata.MessageId);
        return ValueTask.FromResult(Result.Success());
    }
}

/// <summary>
/// Handles <see cref="ProcessPaymentCommand"/> commands to process customer payments.
/// </summary>
public sealed class ProcessPaymentHandler : IMessageHandler<ProcessPaymentCommand>
{
    private readonly ILogger<ProcessPaymentHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessPaymentHandler"/> class.
    /// </summary>
    /// <param name="logger">The logger used to record payment operations.</param>
    public ProcessPaymentHandler(ILogger<ProcessPaymentHandler> logger) => _logger = logger;

    /// <inheritdoc/>
    public ValueTask<Result> HandleAsync(
        ProcessPaymentCommand message, MessageContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "--> [ProcessPaymentHandler] PaymentId={PaymentId} {Amount} {Currency} | MessageId={MessageId}",
            message.PaymentId, message.Amount, message.Currency, context.Metadata.MessageId);
        return ValueTask.FromResult(Result.Success());
    }
}

/// <summary>
/// Handles <see cref="InventoryItemUpdatedEvent"/> messages to update inventory availability.
/// </summary>
public sealed class InventoryItemUpdatedHandler : IMessageHandler<InventoryItemUpdatedEvent>
{
    private readonly ILogger<InventoryItemUpdatedHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InventoryItemUpdatedHandler"/> class.
    /// </summary>
    /// <param name="logger">The logger used to record inventory updates.</param>
    public InventoryItemUpdatedHandler(ILogger<InventoryItemUpdatedHandler> logger) => _logger = logger;

    /// <inheritdoc/>
    public ValueTask<Result> HandleAsync(
        InventoryItemUpdatedEvent message, MessageContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "--> [InventoryItemUpdatedHandler] SKU={Sku} Qty={Qty} Price={Price}",
            message.Sku, message.QuantityAvailable, message.UnitPrice);
        return ValueTask.FromResult(Result.Success());
    }
}

/// <summary>
/// Handles <see cref="NotificationMessage"/> messages to dispatch notifications.
/// </summary>
public sealed class NotificationHandler : IMessageHandler<NotificationMessage>
{
    private readonly ILogger<NotificationHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationHandler"/> class.
    /// </summary>
    /// <param name="logger">The logger used to record dispatched notifications.</param>
    public NotificationHandler(ILogger<NotificationHandler> logger) => _logger = logger;

    /// <inheritdoc/>
    public ValueTask<Result> HandleAsync(
        NotificationMessage message, MessageContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "--> [NotificationHandler] NotificationId={Id} Body={Body}",
            message.NotificationId, message.Body);
        return ValueTask.FromResult(Result.Success());
    }
}

/// <summary>
/// Handles <see cref="BatchItemMessage"/> messages processed within batch pipelines.
/// </summary>
public sealed class BatchItemHandler : IMessageHandler<BatchItemMessage>
{
    private readonly ILogger<BatchItemHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BatchItemHandler"/> class.
    /// </summary>
    /// <param name="logger">The logger used to record batch processing operations.</param>
    public BatchItemHandler(ILogger<BatchItemHandler> logger) => _logger = logger;

    /// <inheritdoc/>
    public ValueTask<Result> HandleAsync(
        BatchItemMessage message, MessageContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "--> [BatchItemHandler] ItemId={ItemId} Op={Operation}",
            message.ItemId, message.Operation);
        return ValueTask.FromResult(Result.Success());
    }
}

/// <summary>
/// Handles <see cref="ScheduledReportMessage"/> messages to execute scheduled reporting tasks.
/// </summary>
public sealed class ScheduledReportHandler : IMessageHandler<ScheduledReportMessage>
{
    private readonly ILogger<ScheduledReportHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduledReportHandler"/> class.
    /// </summary>
    /// <param name="logger">The logger used to record scheduled report operations.</param>
    public ScheduledReportHandler(ILogger<ScheduledReportHandler> logger) => _logger = logger;

    /// <inheritdoc/>
    public ValueTask<Result> HandleAsync(
        ScheduledReportMessage message, MessageContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "--> [ScheduledReportHandler] ReportId={Id} Name={Name} ScheduledAt={At}",
            message.ReportId, message.ReportName, message.ScheduledAt);
        return ValueTask.FromResult(Result.Success());
    }
}

/// <summary>
/// Handles <see cref="ValidateCustomerMessage"/> messages and returns functional validation results.
/// </summary>
public sealed class ValidateCustomerHandler : IMessageHandler<ValidateCustomerMessage>
{
    private readonly ILogger<ValidateCustomerHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidateCustomerHandler"/> class.
    /// </summary>
    /// <param name="logger">The logger used to record customer validation attempts.</param>
    public ValidateCustomerHandler(ILogger<ValidateCustomerHandler> logger) => _logger = logger;

    /// <inheritdoc/>
    public ValueTask<Result> HandleAsync(
        ValidateCustomerMessage message, MessageContext context, CancellationToken cancellationToken = default)
    {
        if (message.CustomerId == Guid.Empty || string.IsNullOrWhiteSpace(message.Email))
        {
            _logger.LogWarning(
                "--> [ValidateCustomerHandler] Validation failed for CustomerId={Id}",
                message.CustomerId);

            return ValueTask.FromResult(Result.Failure(Error.Validation(
                code: "Customer.InvalidData",
                description: "CustomerId cannot be empty and Email must be provided.")));
        }

        _logger.LogInformation(
            "--> [ValidateCustomerHandler] Valid: CustomerId={Id} Email={Email}",
            message.CustomerId, message.Email);

        return ValueTask.FromResult(Result.Success());
    }
}

// =============================================================================
// UPCASTERS — IMessageUpcaster<TOldMessage, TNewMessage>
// =============================================================================

/// <summary>
/// Upgrades <see cref="OrderPlacedEventV1"/> messages to <see cref="OrderPlacedEventV2"/>.
/// </summary>
public sealed class OrderPlacedUpcaster : IMessageUpcaster<OrderPlacedEventV1, OrderPlacedEventV2>
{
    /// <inheritdoc/>
    public OrderPlacedEventV2 Upcast(OrderPlacedEventV1 oldMessage, TransportMessageMetadata metadata)
    {
        return new OrderPlacedEventV2(
            OrderId: oldMessage.OrderId,
            OrderNumber: oldMessage.OrderNumber,
            Amount: oldMessage.Amount,
            Currency: "USD",         // safe default for legacy V1 messages
            Channel: "Legacy-Web"); // safe default for legacy V1 messages
    }
}

// =============================================================================
// PARTITION KEY RESOLVER — IPartitionKeyResolver
// =============================================================================

/// <summary>
/// Resolves partition routing keys for orders without reflection.
/// </summary>
public sealed class CustomOrderPartitionKeyResolver : IPartitionKeyResolver
{
    /// <inheritdoc/>
    public string? Resolve<TMessage>(TMessage message) where TMessage : notnull
    {
        if (message is PartitionedOrderMessage order)
        {
            return $"partition-{order.Region.ToLowerInvariant()}";
        }
        return null;
    }
}


// =============================================================================
// CUSTOM MIDDLEWARE — IMessageMiddleware
// =============================================================================

/// <summary>
/// Intercepts message execution to log audit trail information before and after dispatching.
/// </summary>
public sealed class AuditMiddleware : IMessageMiddleware
{
    /// <inheritdoc/>
    public async ValueTask<Result> InvokeAsync(
        MessageContext context,
        MessageExecutionDelegate next,
        CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $"[AuditMiddleware] → Before: MessageId={context.Metadata.MessageId}, Type={context.Metadata.MessageType}");

        var result = await next(context, cancellationToken);

        Console.WriteLine(
            $"[AuditMiddleware] ← After: MessageId={context.Metadata.MessageId}, Success={result.IsSuccess}");

        return result;
    }
}

// =============================================================================
// CUSTOM TRANSPORT — IMessageTransport + IBatchMessageTransport
// =============================================================================

/// <summary>
/// Provides an in-memory transport implementing individual and batch dispatch capabilities.
/// </summary>
public sealed class CustomBatchTransport : IBatchMessageTransport
{
    private readonly List<(string, ReadOnlyMemory<byte>, TransportMessageMetadata)> _published = new();

    /// <inheritdoc/>
    public ValueTask<Result> PublishRawAsync(
        string destination,
        ReadOnlyMemory<byte> payload,
        TransportMessageMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        _published.Add((destination, payload, metadata));
        Console.WriteLine($"[CustomBatchTransport] PublishRawAsync dest={destination} bytes={payload.Length}");
        return ValueTask.FromResult(Result.Success());
    }

    /// <inheritdoc/>
    public ValueTask<Result> PublishBatchRawAsync(
        string destination,
        IReadOnlyList<(ReadOnlyMemory<byte> Payload, TransportMessageMetadata Metadata)> batch,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[CustomBatchTransport] PublishBatchRawAsync dest={destination} count={batch.Count}");
        foreach (var (payload, meta) in batch)
        {
            _published.Add((destination, payload, meta));
        }
        return ValueTask.FromResult(Result.Success());
    }

    /// <inheritdoc/>
    public ValueTask<Result> SubscribeAsync(
        string destination,
        Func<ReadOnlyMemory<byte>, TransportMessageMetadata, CancellationToken, ValueTask<TransportAckResult>> messageHandler,
        TransportSubscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[CustomBatchTransport] SubscribeAsync dest={destination}");
        return ValueTask.FromResult(Result.Success());
    }

    /// <summary>
    /// Releases the resources used by this instance.
    /// </summary>
    /// <returns>A task representing the asynchronous disposal operation.</returns>
    public ValueTask DisposeAsync()
    {
        _published.Clear();
        return ValueTask.CompletedTask;
    }
}

// =============================================================================
// CUSTOM SERIALIZER — IMessageSerializer
// =============================================================================

/// <summary>
/// Provides XML serialization capabilities implementing the <see cref="IMessageSerializer"/> contract.
/// </summary>
public sealed class CustomXmlSerializer : IMessageSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = ShowcaseJsonContext.Default
    };

    /// <inheritdoc/>
    public string ContentType => "application/xml";

    /// <inheritdoc/>
    public ReadOnlyMemory<byte> Serialize<T>(T message) where T : notnull
    {
        // Real implementation would use System.Xml or a third-party library.
        // Placeholder: using source-generated JSON serializer for AOT compatibility.
        var jsonInfo = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>?)SerializerOptions.GetTypeInfo(typeof(T));
        if (jsonInfo is null)
        {
            // Fallback: serialize as object (not AOT-safe, but illustrative)
            return System.Text.Encoding.UTF8.GetBytes(message.ToString() ?? string.Empty);
        }
        return JsonSerializer.SerializeToUtf8Bytes(message, jsonInfo);
    }

    /// <inheritdoc/>
    public void Serialize<T>(T message, IBufferWriter<byte> writer) where T : notnull
    {
        var bytes = Serialize(message);
        var span = writer.GetSpan(bytes.Length);
        bytes.Span.CopyTo(span);
        writer.Advance(bytes.Length);
    }

    /// <inheritdoc/>
    public T Deserialize<T>(ReadOnlyMemory<byte> bytes)
    {
        var jsonInfo = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>?)SerializerOptions.GetTypeInfo(typeof(T));
        if (jsonInfo is null)
        {
            throw new InvalidOperationException(
                $"No source-generated JsonTypeInfo for '{typeof(T).Name}'. " +
                "Register the type in ShowcaseJsonContext.");
        }
        var result = JsonSerializer.Deserialize(bytes.Span, jsonInfo);
        return result ?? throw new InvalidOperationException($"Deserialization returned null for {typeof(T).Name}.");
    }

    /// <inheritdoc/>
    public object Deserialize(ReadOnlyMemory<byte> bytes, Type messageType)
    {
        var jsonInfo = SerializerOptions.GetTypeInfo(messageType);
        var result = JsonSerializer.Deserialize(bytes.Span, jsonInfo);
        return result ?? throw new InvalidOperationException($"Deserialization returned null for {messageType.Name}.");
    }
}

// =============================================================================
// CUSTOM DEAD-LETTER QUEUE — IDeadLetterQueue
// =============================================================================

/// <summary>
/// Provides an in-memory dead-letter queue store for inspection and testing.
/// </summary>
public sealed class InMemoryDeadLetterQueue : IDeadLetterQueue
{
    private readonly List<(string type, DeadLetterReason reason)> _dlq = new();

    /// <inheritdoc/>
    public ValueTask<Result> ForwardToDeadLetterAsync<TMessage>(
        TMessage message,
        DeadLetterReason reason,
        MessageContext? context = null,
        CancellationToken cancellationToken = default) where TMessage : notnull
    {
        _dlq.Add((typeof(TMessage).Name, reason));
        Console.WriteLine(
            $"[InMemoryDLQ] Forwarded {typeof(TMessage).Name} → DLQ. Reason: {reason.ReasonCode}");
        return ValueTask.FromResult(Result.Success());
    }

    /// <inheritdoc/>
    public ValueTask<Result> ForwardRawToDeadLetterAsync(
        ReadOnlyMemory<byte> rawPayload,
        DeadLetterReason reason,
        TransportMessageMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        _dlq.Add(("raw", reason));
        Console.WriteLine(
            $"[InMemoryDLQ] Forwarded raw payload ({rawPayload.Length} bytes) → DLQ. Reason: {reason.ReasonCode}");
        return ValueTask.FromResult(Result.Success());
    }

    /// <summary>
    /// Gets the total count of messages forwarded to this dead-letter queue.
    /// </summary>
    public int Count => _dlq.Count;
}

// =============================================================================
// NATIVE AOT JSON CONTEXT — Source-generated serialisation for showcase types
// =============================================================================

/// <summary>
/// Provides source-generated JSON serialization metadata for showcase message types.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(PingMessage))]
[JsonSerializable(typeof(OrderFulfilledEvent))]
[JsonSerializable(typeof(ProcessPaymentCommand))]
[JsonSerializable(typeof(InventoryItemUpdatedEvent))]
[JsonSerializable(typeof(NotificationMessage))]
[JsonSerializable(typeof(BatchItemMessage))]
[JsonSerializable(typeof(ScheduledReportMessage))]
[JsonSerializable(typeof(ValidateCustomerMessage))]
[JsonSerializable(typeof(OrderPlacedEventV1))]
[JsonSerializable(typeof(OrderPlacedEventV2))]
[JsonSerializable(typeof(PartitionedOrderMessage))]
public sealed partial class ShowcaseJsonContext : JsonSerializerContext
{
}

