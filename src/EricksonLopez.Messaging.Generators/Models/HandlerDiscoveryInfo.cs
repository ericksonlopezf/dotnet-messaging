// Copyright © Erickson Lopez. MIT License.
namespace EricksonLopez.Messaging.Generators.Models;

internal readonly struct HandlerDiscoveryInfo
{
    public string HandlerFullName { get; }
    public string MessageFullName { get; }
    public string MessageTypeName { get; }
    public string HandlerName { get; }

    public HandlerDiscoveryInfo(
        string handlerFullName,
        string messageFullName,
        string messageTypeName,
        string handlerName)
    {
        HandlerFullName = handlerFullName;
        MessageFullName = messageFullName;
        MessageTypeName = messageTypeName;
        HandlerName = handlerName;
    }
}
