// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;

namespace EricksonLopez.Messaging.Tests.Common;

using System.Collections.Generic;
using EricksonLopez.Messaging.Contracts;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Centralized test factory for creating configured <see cref="MessageContext"/> and <see cref="TransportMessageMetadata"/> instances.
/// </summary>
public static class TestMessageContextFactory
{
    /// <summary>
    /// Creates a new <see cref="MessageContext"/> with customizable metadata and dependencies.
    /// </summary>
    public static MessageContext CreateContext(
        string messageType = "test.event",
        string? correlationId = "corr-test",
        string? causationId = null,
        string? traceParent = null,
        string? tenantId = null,
        string? partitionKey = null,
        IReadOnlyDictionary<string, string>? headers = null,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default)
    {
        var metadata = TransportMessageMetadata.Create(
            messageType: messageType,
            correlationId: correlationId,
            causationId: causationId,
            traceParent: traceParent,
            tenantId: tenantId,
            partitionKey: partitionKey,
            headers: headers);

        if (serviceProvider == null)
        {
            var services = new ServiceCollection();
            serviceProvider = services.BuildServiceProvider();
        }

        return new MessageContext(metadata, serviceProvider, cancellationToken);
    }

    /// <summary>
    /// Creates a new <see cref="MessageContext"/> with a specified cancellation token.
    /// </summary>
    public static MessageContext CreateContext(CancellationToken cancellationToken) =>
        CreateContext("test.event", "corr-test", cancellationToken: cancellationToken);

    /// <summary>
    /// Creates a new <see cref="MessageContext"/> with message type and correlation ID.
    /// </summary>
    public static MessageContext CreateContext(string messageType, string correlationId) =>
        CreateContext(messageType, correlationId, cancellationToken: default);

    /// <summary>
    /// Creates a default <see cref="TransportMessageMetadata"/> instance for test assertions.
    /// </summary>
    public static TransportMessageMetadata CreateMetadata(
        string messageType = "test.event",
        string? correlationId = "corr-test",
        string? causationId = null,
        string? traceParent = null,
        string? tenantId = null,
        string? partitionKey = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        return TransportMessageMetadata.Create(
            messageType: messageType,
            correlationId: correlationId,
            causationId: causationId,
            traceParent: traceParent,
            tenantId: tenantId,
            partitionKey: partitionKey,
            headers: headers);
    }
}



