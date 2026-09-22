// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Result;

namespace EricksonLopez.Messaging.Serialization;

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using EricksonLopez.Messaging.Contracts;
using Microsoft.Extensions.Options;

/// <summary>
/// Provides JSON serialization and deserialization supporting Native AOT through <see cref="IJsonTypeInfoResolver"/> and <see cref="JsonSerializerOptions"/>.
/// </summary>
public sealed class NativeAotJsonSerializer : IMessageSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <inheritdoc />
    public string ContentType => "application/json";

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeAotJsonSerializer"/> class with default Native AOT source-generated metadata contexts.
    /// </summary>
    public NativeAotJsonSerializer() : this((IJsonTypeInfoResolver?)null, (IOptions<JsonSerializerOptions>?)null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeAotJsonSerializer"/> class with the specified type info resolver.
    /// </summary>
    /// <param name="resolver">The source-generated type info resolver.</param>
    public NativeAotJsonSerializer(IJsonTypeInfoResolver resolver) : this(resolver, (IOptions<JsonSerializerOptions>?)null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeAotJsonSerializer"/> class with the specified resolver and options.
    /// </summary>
    /// <param name="resolver">The source-generated type info resolver, if specified.</param>
    /// <param name="options">The JSON serializer options from dependency injection, if specified.</param>
    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public NativeAotJsonSerializer(IJsonTypeInfoResolver? resolver, IOptions<JsonSerializerOptions>? options = null)
    {
        if (options?.Value is not null)
        {
            _options = options.Value;
        }
        else if (resolver is not null)
        {
            _options = CreateOptions(resolver);
        }
        else
        {
            _options = CreateDefaultOptions();
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeAotJsonSerializer"/> class with the specified serializer options.
    /// </summary>
    /// <param name="options">The JSON serializer options configured with source-generated contexts, if specified.</param>
    public NativeAotJsonSerializer(JsonSerializerOptions? options = null)
    {
        _options = options ?? CreateDefaultOptions();
    }

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = MessagingJsonContext.Default
        };
    }

    private static JsonSerializerOptions CreateOptions(IJsonTypeInfoResolver resolver)
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = JsonTypeInfoResolver.Combine(resolver, MessagingJsonContext.Default)
        };
    }

    /// <inheritdoc />
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "AOT-safe through TypeInfoResolver.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "AOT-safe through TypeInfoResolver.")]
    public ReadOnlyMemory<byte> Serialize<T>(T message) where T : notnull
    {
        return JsonSerializer.SerializeToUtf8Bytes<T>(message, _options);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/></exception>
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "AOT-safe through TypeInfoResolver.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "AOT-safe through TypeInfoResolver.")]
    public void Serialize<T>(T message, IBufferWriter<byte> writer) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(writer);
        using var utf8Writer = new Utf8JsonWriter(writer);
        JsonSerializer.Serialize<T>(utf8Writer, message, _options);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException"><paramref name="bytes"/> is empty, or deserialization produced a <see langword="null"/> value for <typeparamref name="T"/></exception>
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "AOT-safe through TypeInfoResolver.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "AOT-safe through TypeInfoResolver.")]
    public T Deserialize<T>(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            throw new InvalidOperationException("Cannot deserialize empty byte buffer.");
        }

        var result = JsonSerializer.Deserialize<T>(bytes.Span, _options);
        if (result is null)
        {
            throw new InvalidOperationException($"Deserialization produced null value for type '{typeof(T).FullName}'.");
        }

        return result;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="messageType"/> is <see langword="null"/></exception>
    /// <exception cref="InvalidOperationException"><paramref name="bytes"/> is empty, or deserialization produced a <see langword="null"/> value for <paramref name="messageType"/></exception>
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "AOT-safe through TypeInfoResolver.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "AOT-safe through TypeInfoResolver.")]
    public object Deserialize(ReadOnlyMemory<byte> bytes, Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        if (bytes.IsEmpty)
        {
            throw new InvalidOperationException("Cannot deserialize empty byte buffer.");
        }

        var result = JsonSerializer.Deserialize(bytes.Span, messageType, _options);
        if (result is null)
        {
            throw new InvalidOperationException($"Deserialization produced null value for type '{messageType.FullName}'.");
        }

        return result;
    }
}



