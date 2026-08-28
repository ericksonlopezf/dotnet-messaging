// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;

namespace EricksonLopez.Messaging.AzureServiceBus.Tests.Common;

using System.Reflection;
using Azure.Messaging.ServiceBus;

/// <summary>
/// Dedicated test infrastructure factory for Azure Service Bus SDK types.
/// Note: <see cref="ServiceBusModelFactory"/> provides mocks for entity models (like <see cref="ServiceBusReceivedMessage"/>),
/// but event argument types such as <see cref="ProcessMessageEventArgs"/> have internal constructors in the Azure SDK.
/// This factory resolves and invokes the SDK internal constructor deterministically with fallback.
/// </summary>
public static class AzureServiceBusTestFactory
{
    private static readonly ConstructorInfo? ProcessMessageEventArgsCtor = typeof(ProcessMessageEventArgs).GetConstructor(
        BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
        binder: null,
        types: new[] { typeof(ServiceBusReceivedMessage), typeof(ServiceBusReceiver), typeof(CancellationToken) },
        modifiers: null);

    /// <summary>
    /// Creates a simulated <see cref="ProcessMessageEventArgs"/> instance using SDK constructor resolution.
    /// </summary>
    public static ProcessMessageEventArgs CreateProcessMessageEventArgs(
        ServiceBusReceivedMessage message,
        ServiceBusReceiver receiver,
        CancellationToken cancellationToken)
    {
        if (ProcessMessageEventArgsCtor != null)
        {
            return (ProcessMessageEventArgs)ProcessMessageEventArgsCtor.Invoke(new object[] { message, receiver, cancellationToken });
        }

        var ctors = typeof(ProcessMessageEventArgs).GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        foreach (var ctor in ctors)
        {
            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var pType = parameters[i].ParameterType;
                if (pType == typeof(ServiceBusReceivedMessage)) args[i] = message;
                else if (pType == typeof(ServiceBusReceiver)) args[i] = receiver;
                else if (pType == typeof(CancellationToken)) args[i] = cancellationToken;
                else if (pType == typeof(string)) args[i] = "test-entity";
                else if (pType.IsValueType) args[i] = Activator.CreateInstance(pType);
                else args[i] = null;
            }

            try
            {
                return (ProcessMessageEventArgs)ctor.Invoke(args);
            }
            catch
            {
                // Try next available constructor signature
            }
        }

        throw new InvalidOperationException("Unable to construct ProcessMessageEventArgs instance for Azure Service Bus tests.");
    }
}


