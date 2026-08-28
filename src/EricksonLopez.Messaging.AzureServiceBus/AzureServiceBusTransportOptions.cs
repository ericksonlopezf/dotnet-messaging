// Copyright © Erickson Lopez. MIT License.
namespace EricksonLopez.Messaging.Transport.AzureServiceBus;

/// <summary>
/// Specifies configuration options for the Azure Service Bus transport.
/// </summary>
public sealed class AzureServiceBusTransportOptions
{
    /// <summary>
    /// Gets or sets the Azure Service Bus connection string for shared access signature authentication.
    /// </summary>
    /// <remarks>Mutually exclusive with <see cref="FullyQualifiedNamespace"/>. When both are set, the connection string takes precedence.</remarks>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the fully-qualified namespace (e.g., <c>mynamespace.servicebus.windows.net</c>) for token credential authentication.
    /// </summary>
    /// <remarks>Use this property together with <see cref="Credential"/> when authenticating via managed identity or a custom credential. Ignored when <see cref="ConnectionString"/> is set.</remarks>
    public string FullyQualifiedNamespace { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the explicit token credential for authentication when using <see cref="FullyQualifiedNamespace"/>.
    /// </summary>
    public global::Azure.Core.TokenCredential? Credential { get; set; }
}

