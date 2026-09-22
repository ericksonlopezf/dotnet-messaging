// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace EricksonLopez.Messaging.Testing;

/// <summary>
/// Represents a collection of messages consumed by subscription handlers during test execution.
/// </summary>
public sealed class ConsumedMessageList : IReadOnlyList<ConsumedMessage>
{
    private readonly IReadOnlyList<ConsumedMessage> _messages;

    internal ConsumedMessageList(IEnumerable<ConsumedMessage> messages)
    {
        _messages = messages is IReadOnlyList<ConsumedMessage> list ? list : messages.ToList();
    }

    /// <summary>
    /// Gets the number of consumed messages in the collection.
    /// </summary>
    public int Count => _messages.Count;

    /// <summary>
    /// Gets the consumed message at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the message to get.</param>
    /// <returns>The consumed message at the specified index.</returns>
    public ConsumedMessage this[int index] => _messages[index];

    /// <summary>
    /// Determines whether any consumed message matches the specified message type.
    /// </summary>
    /// <param name="messageType">The message type identifier to locate.</param>
    /// <returns><see langword="true"/> if a matching message was consumed; otherwise, <see langword="false"/>.</returns>
    public bool Contains(string messageType)
    {
        return _messages.Any(m => m.Metadata.MessageType.Equals(messageType, StringComparison.Ordinal));
    }

    /// <summary>
    /// Filters the consumed messages by the specified message type.
    /// </summary>
    /// <param name="messageType">The message type identifier to filter by.</param>
    /// <returns>An enumerable collection of matching consumed messages.</returns>
    public IEnumerable<ConsumedMessage> OfType(string messageType)
    {
        return _messages.Where(m => m.Metadata.MessageType.Equals(messageType, StringComparison.Ordinal));
    }

    /// <summary>
    /// Determines whether any consumed message with the specified type was acknowledged successfully.
    /// </summary>
    /// <param name="messageType">The message type identifier to evaluate.</param>
    /// <returns><see langword="true"/> if at least one matching message succeeded; otherwise, <see langword="false"/>.</returns>
    public bool AnySucceeded(string messageType)
    {
        return _messages.Any(m =>
            m.Metadata.MessageType.Equals(messageType, StringComparison.Ordinal) && m.Succeeded);
    }

    /// <inheritdoc />
    public IEnumerator<ConsumedMessage> GetEnumerator() => _messages.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
