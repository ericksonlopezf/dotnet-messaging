// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace EricksonLopez.Messaging.Testing;

/// <summary>
/// Represents a collection of messages published through an <see cref="InMemoryTestHarness"/> during test execution.
/// </summary>
public sealed class PublishedMessageList : IReadOnlyList<PublishedMessage>
{
    private readonly IReadOnlyList<PublishedMessage> _messages;

    internal PublishedMessageList(IEnumerable<PublishedMessage> messages)
    {
        _messages = messages is IReadOnlyList<PublishedMessage> list ? list : messages.ToList();
    }

    /// <summary>
    /// Gets the number of published messages in the collection.
    /// </summary>
    public int Count => _messages.Count;

    /// <summary>
    /// Gets the published message at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the message to get.</param>
    /// <returns>The published message at the specified index.</returns>
    public PublishedMessage this[int index] => _messages[index];

    /// <summary>
    /// Determines whether any published message matches the specified message type.
    /// </summary>
    /// <param name="messageType">The message type identifier to locate.</param>
    /// <returns><see langword="true"/> if a matching message was published; otherwise, <see langword="false"/>.</returns>
    public bool Contains(string messageType)
    {
        return _messages.Any(m => m.Metadata.MessageType.Equals(messageType, StringComparison.Ordinal));
    }

    /// <summary>
    /// Filters the published messages by the specified message type.
    /// </summary>
    /// <param name="messageType">The message type identifier to filter by.</param>
    /// <returns>An enumerable collection of matching published messages.</returns>
    public IEnumerable<PublishedMessage> OfType(string messageType)
    {
        return _messages.Where(m => m.Metadata.MessageType.Equals(messageType, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public IEnumerator<PublishedMessage> GetEnumerator() => _messages.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
