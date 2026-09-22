// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Messaging.Contracts;

/// <summary>
/// Provides a mechanism to extract partition keys from messages without using reflection.
/// </summary>
/// <remarks>
/// This interface is typically implemented via source generators for Native AOT compatibility.
/// </remarks>
public interface IPartitionKeyResolver
{
    /// <summary>
    /// Attempts to extract a partition key from the provided message.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message.</typeparam>
    /// <param name="message">The message instance.</param>
    /// <returns>The extracted partition key, or <see langword="null"/> if the message does not define one or cannot be extracted.</returns>
    string? Resolve<TMessage>(TMessage message) where TMessage : notnull;
}
