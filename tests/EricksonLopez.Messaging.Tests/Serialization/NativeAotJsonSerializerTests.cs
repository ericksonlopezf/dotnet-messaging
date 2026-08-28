// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;

namespace EricksonLopez.Messaging.Tests.Serialization;

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Serialization;
using FsCheck;
using FsCheck.Xunit;
using NSubstitute;
using Xunit;

[Trait("Category", "Unit")]
public class NativeAotJsonSerializerTests
{
    public sealed record CustomerRegistered(string Email, string FullName) : IMessage;
    public sealed record OrderPlaced(string OrderId, decimal Amount, int Quantity) : IMessage;

    public sealed record ComplexOrderPayload(string OrderId, decimal Amount, int[] ItemIds) : IMessage;

    [Fact]
    public void ContentType_WhenAccessed_ReturnsApplicationJson()
    {
        // Arrange
        var serializer = new NativeAotJsonSerializer();

        // Assert
        serializer.ContentType.Should().Be("application/json");
    }

    [Fact]
    public void Constructor_DefaultOptions_UsesCamelCaseNaming()
    {
        // Arrange
        var serializer = new NativeAotJsonSerializer();
        var original = new CustomerRegistered("test@ericksonlopez.dev", "Erickson Lopez");

        // Act
        var bytes = serializer.Serialize(original);
        var jsonString = Encoding.UTF8.GetString(bytes.Span);

        // Assert
        jsonString.Should().Contain("\"email\":");
        jsonString.Should().Contain("\"fullName\":");
        jsonString.Should().NotContain("\"Email\":");
        jsonString.Should().NotContain("\"FullName\":");
    }

    [Fact]
    public void Constructor_DefaultOptions_IsCaseInsensitiveOnDeserialization()
    {
        // Arrange
        var serializer = new NativeAotJsonSerializer();
        var jsonWithUppercase = Encoding.UTF8.GetBytes("{\"EMAIL\":\"upper@test.com\",\"FULLNAME\":\"Upper Case\"}");

        // Act
        var deserialized = serializer.Deserialize<CustomerRegistered>(jsonWithUppercase);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Email.Should().Be("upper@test.com");
        deserialized.FullName.Should().Be("Upper Case");
    }

    public sealed record NullableCustomer(string Name, string? Nickname) : IMessage;

    [Fact]
    public void Constructor_DefaultOptions_IgnoresNullValuesWhenSerializing()
    {
        // Arrange
        var serializer = new NativeAotJsonSerializer();
        var original = new NullableCustomer("John Doe", null);

        // Act
        var bytes = serializer.Serialize(original);
        var jsonString = Encoding.UTF8.GetString(bytes.Span);

        // Assert
        jsonString.Should().Contain("\"name\":");
        jsonString.Should().NotContain("\"nickname\"");
    }

    [Fact]
    public void Constructor_DefaultOptions_ResolvesSourceGeneratedMessagingJsonContext()
    {
        // Arrange
        var serializer = new NativeAotJsonSerializer();
        var metadata = TransportMessageMetadata.Create("test.event", "corr-999");

        // Act
        var bytes = serializer.Serialize(metadata);
        var deserialized = serializer.Deserialize<TransportMessageMetadata>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.MessageType.Should().Be("test.event");
        deserialized.CorrelationId.Should().Be("corr-999");
    }

    [Fact]
    public void Constructor_WithCustomOptions_UsesProvidedOptions()
    {
        // Arrange
        var customOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null // PascalCase
        };
        var serializer = new NativeAotJsonSerializer(customOptions);
        var original = new CustomerRegistered("test@custom.com", "Custom User");

        // Act
        var bytes = serializer.Serialize(original);
        var jsonString = Encoding.UTF8.GetString(bytes.Span);

        // Assert
        jsonString.Should().Contain("\"Email\":");
        jsonString.Should().Contain("\"FullName\":");
    }

    [Fact]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test relies on DefaultJsonTypeInfoResolver")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test relies on DefaultJsonTypeInfoResolver")]
    public void Constructor_WithIJsonTypeInfoResolver_UsesProvidedResolver()
    {
        // Arrange
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(ti =>
        {
            if (ti.Type == typeof(CustomerRegistered))
            {
                foreach (var prop in ti.Properties)
                {
                    prop.Name = "custom_" + prop.Name;
                }
            }
        });
        var serializer = new NativeAotJsonSerializer(resolver);
        var original = new CustomerRegistered("test@resolver.com", "Resolver User");

        // Act
        var bytes = serializer.Serialize(original);
        var jsonString = Encoding.UTF8.GetString(bytes.Span);

        // Assert
        jsonString.Should().Contain("\"custom_email\":");
        jsonString.Should().Contain("\"custom_fullName\":");
    }

    [Fact]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test relies on DefaultJsonTypeInfoResolver")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test relies on DefaultJsonTypeInfoResolver")]
    public void Constructor_WithIOptionsAndResolver_UsesProvidedOptions()
    {
        // Arrange
        var customOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower
        };
        var optionsMock = Microsoft.Extensions.Options.Options.Create(customOptions);
        var resolver = new DefaultJsonTypeInfoResolver();
        var serializer = new NativeAotJsonSerializer(resolver, optionsMock);
        var original = new CustomerRegistered("test@options.com", "Options User");

        // Act
        var bytes = serializer.Serialize(original);
        var jsonString = Encoding.UTF8.GetString(bytes.Span);

        // Assert
        jsonString.Should().Contain("\"email\":");
        jsonString.Should().Contain("\"full-name\":");
    }

    [Fact]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test relies on DefaultJsonTypeInfoResolver")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test relies on DefaultJsonTypeInfoResolver")]
    public void Constructor_WithIOptionsNullAndResolver_UsesResolver()
    {
        // Arrange
        var resolver = new DefaultJsonTypeInfoResolver();
        var serializer = new NativeAotJsonSerializer(resolver, (Microsoft.Extensions.Options.IOptions<JsonSerializerOptions>?)null);
        var original = new CustomerRegistered("test@options.com", "Options User");

        // Act
        var bytes = serializer.Serialize(original);
        var jsonString = Encoding.UTF8.GetString(bytes.Span);

        // Assert
        jsonString.Should().Contain("\"email\":");
        jsonString.Should().Contain("\"fullName\":");
    }

    [Fact]
    public void Constructor_WithNullJsonSerializerOptions_UsesDefaultOptions()
    {
        // Arrange & Act
        var serializer = new NativeAotJsonSerializer((JsonSerializerOptions?)null);
        var original = new CustomerRegistered("nullopt@test.com", "Null Opt");

        // Act
        var bytes = serializer.Serialize(original);
        var jsonString = Encoding.UTF8.GetString(bytes.Span);

        // Assert
        jsonString.Should().Contain("\"email\":");
        jsonString.Should().Contain("\"fullName\":");
    }

    [Fact]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test relies on DefaultJsonTypeInfoResolver")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test relies on DefaultJsonTypeInfoResolver")]
    public void Constructor_WithOptionsHavingNullValue_UsesResolver()
    {
        // Arrange
        var resolver = new DefaultJsonTypeInfoResolver();
        var optionsMock = NSubstitute.Substitute.For<Microsoft.Extensions.Options.IOptions<JsonSerializerOptions>>();
        optionsMock.Value.Returns((JsonSerializerOptions)null!);
        var serializer = new NativeAotJsonSerializer(resolver, optionsMock);
        var original = new CustomerRegistered("optnull@test.com", "Opt Null");

        // Act
        var bytes = serializer.Serialize(original);
        var jsonString = Encoding.UTF8.GetString(bytes.Span);

        // Assert
        jsonString.Should().Contain("\"email\":");
        jsonString.Should().Contain("\"fullName\":");
    }

    [Fact]
    public void Constructor_WithOptionsHavingNullValueAndNullResolver_UsesDefaultOptions()
    {
        // Arrange
        var optionsMock = NSubstitute.Substitute.For<Microsoft.Extensions.Options.IOptions<JsonSerializerOptions>>();
        optionsMock.Value.Returns((JsonSerializerOptions)null!);
        var serializer = new NativeAotJsonSerializer((IJsonTypeInfoResolver?)null, optionsMock);
        var original = new CustomerRegistered("optnull2@test.com", "Opt Null 2");

        // Act
        var bytes = serializer.Serialize(original);
        var jsonString = Encoding.UTF8.GetString(bytes.Span);

        // Assert
        jsonString.Should().Contain("\"email\":");
        jsonString.Should().Contain("\"fullName\":");
    }

    [Fact]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test relies on DefaultJsonTypeInfoResolver")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test relies on DefaultJsonTypeInfoResolver")]
    public void Constructor_WithResolver_IgnoresNullValues()
    {
        // Arrange
        var resolver = new DefaultJsonTypeInfoResolver();
        var serializer = new NativeAotJsonSerializer(resolver);
        var original = new NullableCustomer("John Resolver", null);

        // Act
        var bytes = serializer.Serialize(original);
        var jsonString = Encoding.UTF8.GetString(bytes.Span);

        // Assert
        jsonString.Should().Contain("\"name\":");
        jsonString.Should().NotContain("\"nickname\"");
    }

    [Fact]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test relies on DefaultJsonTypeInfoResolver")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test relies on DefaultJsonTypeInfoResolver")]
    public void Constructor_WithResolver_IsCaseInsensitiveOnDeserialization()
    {
        // Arrange
        var resolver = new DefaultJsonTypeInfoResolver();
        var serializer = new NativeAotJsonSerializer(resolver);
        var jsonWithUppercase = Encoding.UTF8.GetBytes("{\"EMAIL\":\"upper@test.com\",\"FULLNAME\":\"Upper Case\"}");

        // Act
        var deserialized = serializer.Deserialize<CustomerRegistered>(jsonWithUppercase);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Email.Should().Be("upper@test.com");
        deserialized.FullName.Should().Be("Upper Case");
    }

    [Fact]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test relies on DefaultJsonTypeInfoResolver")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test relies on DefaultJsonTypeInfoResolver")]
    public void Constructor_WithResolver_CombinesWithMessagingJsonContext()
    {
        // Arrange
        var resolver = new DefaultJsonTypeInfoResolver();
        var serializer = new NativeAotJsonSerializer(resolver);
        var metadata = TransportMessageMetadata.Create("resolver.metadata", "corr-resolver");

        // Act
        var bytes = serializer.Serialize(metadata);
        var deserialized = serializer.Deserialize<TransportMessageMetadata>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.MessageType.Should().Be("resolver.metadata");
        deserialized.CorrelationId.Should().Be("corr-resolver");
    }

    [Fact]
    public void SerializeAndDeserialize_Generic_RoundtripsSuccessfully()
    {
        // Arrange
        var serializer = new NativeAotJsonSerializer();
        var original = new CustomerRegistered("test@ericksonlopez.dev", "Erickson Lopez");

        // Act
        var bytes = serializer.Serialize(original);
        var deserialized = serializer.Deserialize<CustomerRegistered>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Email.Should().Be(original.Email);
        deserialized.FullName.Should().Be(original.FullName);
    }

    [Fact]
    public void Serialize_WithBufferWriter_SerializesCorrectly()
    {
        // Arrange
        var serializer = new NativeAotJsonSerializer();
        var original = new CustomerRegistered("writer@ericksonlopez.dev", "Writer Test");
        var bufferWriter = new ArrayBufferWriter<byte>();

        // Act
        serializer.Serialize(original, bufferWriter);
        var deserialized = serializer.Deserialize<CustomerRegistered>(bufferWriter.WrittenMemory);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Email.Should().Be(original.Email);
        deserialized.FullName.Should().Be(original.FullName);
    }

    [Fact]
    public void Serialize_WithNullBufferWriter_ThrowsArgumentNullException()
    {
        // Arrange
        var serializer = new NativeAotJsonSerializer();
        var original = new CustomerRegistered("test@test.com", "Test");

        // Act
        Action act = () => serializer.Serialize(original, (IBufferWriter<byte>)null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("writer");
    }

    [Fact]
    public void SerializeAndDeserialize_NonGenericType_RoundtripsSuccessfully()
    {
        // Arrange
        var serializer = new NativeAotJsonSerializer();
        var original = new CustomerRegistered("admin@ericksonlopez.dev", "Admin User");

        // Act
        var bytes = serializer.Serialize(original);
        var deserialized = serializer.Deserialize(bytes, typeof(CustomerRegistered));

        // Assert
        deserialized.Should().NotBeNull();
        var typed = deserialized.Should().BeOfType<CustomerRegistered>().Subject;
        typed.Email.Should().Be(original.Email);
    }

    [Fact]
    public void Deserialize_Generic_EmptyBytes_ThrowsInvalidOperationException()
    {
        // Arrange
        var serializer = new NativeAotJsonSerializer();

        // Act
        Action act = () => serializer.Deserialize<CustomerRegistered>(ReadOnlyMemory<byte>.Empty);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot deserialize empty byte buffer.");
    }

    [Fact]
    public void Constructor_WithNullResolverAndNonNullOptions_UsesProvidedOptions()
    {
        // Arrange
        var customOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower
        };
        var optionsMock = NSubstitute.Substitute.For<Microsoft.Extensions.Options.IOptions<JsonSerializerOptions>>();
        optionsMock.Value.Returns(customOptions);
        var serializer = new NativeAotJsonSerializer((IJsonTypeInfoResolver?)null, optionsMock);
        var original = new CustomerRegistered("nullresolver@test.com", "Null Resolver");

        // Act
        var bytes = serializer.Serialize(original);
        var jsonString = Encoding.UTF8.GetString(bytes.Span);

        // Assert
        jsonString.Should().Contain("\"email\":");
        jsonString.Should().Contain("\"full-name\":");
    }

    [Fact]
    public void Deserialize_Generic_JsonNull_ThrowsInvalidOperationException()
    {
        // Arrange
        var serializer = new NativeAotJsonSerializer();
        var nullBytes = Encoding.UTF8.GetBytes("null");

        // Act
        Action act = () => serializer.Deserialize<CustomerRegistered>(nullBytes);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Deserialization produced null value for type '{typeof(CustomerRegistered).FullName}'.");
    }

    [Fact]
    public void Deserialize_NonGeneric_NullMessageType_ThrowsArgumentNullException()
    {
        // Arrange
        var serializer = new NativeAotJsonSerializer();
        var bytes = serializer.Serialize(new CustomerRegistered("a@b.com", "AB"));

        // Act
        Action act = () => serializer.Deserialize(bytes, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("messageType");
    }

    [Fact]
    public void Deserialize_NonGeneric_EmptyBytes_ThrowsInvalidOperationException()
    {
        // Arrange
        var serializer = new NativeAotJsonSerializer();

        // Act
        Action act = () => serializer.Deserialize(ReadOnlyMemory<byte>.Empty, typeof(CustomerRegistered));

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot deserialize empty byte buffer.");
    }

    [Fact]
    public void Deserialize_NonGeneric_JsonNull_ThrowsInvalidOperationException()
    {
        // Arrange
        var serializer = new NativeAotJsonSerializer();
        var nullBytes = Encoding.UTF8.GetBytes("null");

        // Act
        Action act = () => serializer.Deserialize(nullBytes, typeof(CustomerRegistered));

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Deserialization produced null value for type '{typeof(CustomerRegistered).FullName}'.");
    }

    [Fact]
    public void MessagingJsonContext_WithMessageMetadata_SerializesMetadataSuccessfully()
    {
        // Arrange
        var metadata = TransportMessageMetadata.Create(
            messageType: "test.metadata.v1",
            correlationId: "c-123",
            headers: new Dictionary<string, string> { ["h1"] = "v1" });

        // Act
        var json = JsonSerializer.Serialize(metadata, MessagingJsonContext.Default.TransportMessageMetadata);
        var deserialized = JsonSerializer.Deserialize(json, MessagingJsonContext.Default.TransportMessageMetadata);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.MessageType.Should().Be("test.metadata.v1");
        deserialized.CorrelationId.Should().Be("c-123");
        deserialized.Headers.Should().NotBeNull();
        deserialized.Headers!["h1"].Should().Be("v1");
    }

    [Fact]
    public void MessagingJsonContext_WithDictionary_SerializesDictionarySuccessfully()
    {
        // Arrange
        var dict = new Dictionary<string, string> { ["k1"] = "v1", ["k2"] = "v2" };

        // Act
        var json = JsonSerializer.Serialize(dict, MessagingJsonContext.Default.DictionaryStringString);
        var deserialized = JsonSerializer.Deserialize(json, MessagingJsonContext.Default.DictionaryStringString);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Should().Equal(dict);
    }

    [Fact]
    public void MessagingJsonContext_WithReadOnlyDictionary_SerializesReadOnlyDictionarySuccessfully()
    {
        // Arrange
        IReadOnlyDictionary<string, string> dict = new Dictionary<string, string> { ["k1"] = "v1" };

        // Act
        var json = JsonSerializer.Serialize(dict, MessagingJsonContext.Default.IReadOnlyDictionaryStringString);
        var deserialized = JsonSerializer.Deserialize(json, MessagingJsonContext.Default.IReadOnlyDictionaryStringString);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Should().Equal(dict);
    }

    [Property]
    public bool SerializeAndDeserialize_ArbitraryOrderPlaced_RoundtripsSuccessfully(NonNull<string> id, int quantity)
    {
        if (string.IsNullOrWhiteSpace(id.Get)) return true;

        var serializer = new NativeAotJsonSerializer();
        var original = new OrderPlaced(id.Get, 99.99m, quantity);

        var bytes = serializer.Serialize(original);
        var deserialized = serializer.Deserialize<OrderPlaced>(bytes);

        return deserialized.OrderId == original.OrderId &&
               deserialized.Amount == original.Amount &&
               deserialized.Quantity == original.Quantity;
    }

    [Property]
    public bool SerializeAndDeserialize_ArbitraryComplexPayload_RoundtripsSuccessfully(NonNull<string> id, int[] itemIds)
    {
        if (string.IsNullOrWhiteSpace(id.Get)) return true;

        var serializer = new NativeAotJsonSerializer();
        var original = new ComplexOrderPayload(id.Get, 199.50m, itemIds ?? Array.Empty<int>());

        var bytes = serializer.Serialize(original);
        var deserialized = serializer.Deserialize<ComplexOrderPayload>(bytes);

        return deserialized.OrderId == original.OrderId &&
               deserialized.Amount == original.Amount &&
               ((original.ItemIds == null && deserialized.ItemIds == null) ||
                (original.ItemIds != null && deserialized.ItemIds != null && original.ItemIds.Length == deserialized.ItemIds.Length));
    }
}



