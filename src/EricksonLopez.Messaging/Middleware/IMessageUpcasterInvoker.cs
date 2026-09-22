// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.Middleware;

using EricksonLopez.Messaging.Contracts;

/// <summary>
/// Defines non-generic invocation of registered message upcasters during pipeline execution.
/// </summary>
public interface IMessageUpcasterInvoker
{
    /// <summary>
    /// Gets the source message type from which this upcaster transforms.
    /// </summary>
    Type SourceType { get; }

    /// <summary>
    /// Gets the target message type to which this upcaster transforms.
    /// </summary>
    Type TargetType { get; }

    /// <summary>
    /// Transforms the source message instance to its upgraded schema target.
    /// </summary>
    /// <param name="message">The source message instance.</param>
    /// <param name="metadata">The message metadata.</param>
    /// <param name="serviceProvider">The service provider for dependency resolution.</param>
    /// <returns>The upgraded message instance.</returns>
    object Upcast(object message, TransportMessageMetadata metadata, IServiceProvider serviceProvider);
}
