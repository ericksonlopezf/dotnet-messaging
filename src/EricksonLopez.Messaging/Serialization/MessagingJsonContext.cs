// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;

namespace EricksonLopez.Messaging.Serialization;

using System.Text.Json.Serialization;
using EricksonLopez.Messaging.Contracts;

/// <summary>
/// Provides a source-generated <see cref="JsonSerializerContext"/> enabling Native AOT serialization for messaging metadata types.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(TransportMessageMetadata))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public sealed partial class MessagingJsonContext : JsonSerializerContext
{
}



