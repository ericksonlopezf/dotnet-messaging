// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;

namespace EricksonLopez.Messaging.Contracts;


/// <summary>
/// Encapsulates ambient execution context supplied to message handlers and middleware during message consumption.
/// </summary>
public sealed class MessageContext
{
    /// <summary>
    /// Gets the metadata associated with the current message.
    /// </summary>
    public TransportMessageMetadata Metadata { get; }

    /// <summary>
    /// Gets the scoped service provider configured for this execution scope.
    /// </summary>
    public IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// Gets or sets the cancellation token that signals the current message execution should be abandoned.
    /// </summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>
    /// Gets or sets the deserialized message payload instance, which middleware components may replace during upcasting.
    /// </summary>
    /// <remarks>
    /// Middleware components such as the message upcasting middleware may assign
    /// an upgraded schema version to this property before the terminal handler is invoked.
    /// </remarks>
    public object? Message { get; set; }

    private IDictionary<string, object?>? _items;

    /// <summary>
    /// Gets a mutable dictionary for sharing arbitrary state between middleware components and the terminal handler within a single message execution.
    /// </summary>
    /// <remarks>Keys are compared using ordinal string comparison. Never returns <see langword="null"/>. Lazily initialized on first access.</remarks>
    public IDictionary<string, object?> Items => _items ??= new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageContext"/> class with the specified metadata and service provider.
    /// </summary>
    /// <param name="metadata">The transport metadata associated with the incoming message.</param>
    /// <param name="serviceProvider">The scoped service provider for dependency resolution within the message execution scope.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the message processing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="metadata"/> or <paramref name="serviceProvider"/> is <see langword="null"/></exception>
    public MessageContext(
        TransportMessageMetadata metadata,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        CancellationToken = cancellationToken;
    }
}


