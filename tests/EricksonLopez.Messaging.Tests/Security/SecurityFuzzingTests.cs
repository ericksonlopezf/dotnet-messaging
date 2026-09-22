// Copyright © Erickson Lopez. MIT License.
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Tests.Security;

using AwesomeAssertions;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Dispatch;
using EricksonLopez.Messaging.Serialization;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Trait("Category", "Security")]
public class SecurityFuzzingTests
{
    [MessageType("security.test.v1")]
    public sealed record SecurityTestMessage(string Data) : IMessage;

    public sealed class SecurityTestHandler : IMessageHandler<SecurityTestMessage>
    {
        public ValueTask<Result> HandleAsync(
            SecurityTestMessage message,
            MessageContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Result.Success());
        }
    }

    [Fact]
    public void Fuzz_RandomBinaryPayloads_HandledGracefullyWithoutCrash()
    {
        // Arrange
        var serializer = new NativeAotJsonSerializer(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        var rng = RandomNumberGenerator.Create();

        // Act & Assert: Fuzz 50 random byte arrays of various lengths
        for (int i = 0; i < 50; i++)
        {
            int length = (i % 20 == 0) ? 0 : (i * 37) % 1024 + 1;
            var buffer = new byte[length];
            rng.GetBytes(buffer);

            // Deserialization must either return an object or throw JsonException, but NEVER crash the process
            Action act = () => serializer.Deserialize<SecurityTestMessage>(buffer);
            act.Should().Throw<Exception>().Which.Should().Match(ex =>
                ex is JsonException || ex is ArgumentException || ex is InvalidOperationException);
        }
    }

    [Fact]
    public void Security_PolymorphicTypeInjection_RejectedWithoutTypeActivation()
    {
        // Arrange: Malicious payload attempting polymorphic gadget activation via $type
        string maliciousJson = """
        {
            "$type": "System.Diagnostics.Process, System",
            "Data": "exploit"
        }
        """;
        var bytes = Encoding.UTF8.GetBytes(maliciousJson);
        var serializer = new NativeAotJsonSerializer(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());

        // Act
        var result = serializer.Deserialize<SecurityTestMessage>(bytes);

        // Assert: Deserialized into strongly-typed target contract, ignoring any arbitrary $type gadget
        result.Should().NotBeNull();
        result.Data.Should().Be("exploit");
        result.Should().BeOfType<SecurityTestMessage>();
    }

    [Fact]
    public void Security_DeeplyNestedPayload_FailsSafelyWithoutStackOverflow()
    {
        // Arrange: Generate 128 levels of nested JSON objects
        var sb = new StringBuilder();
        for (int i = 0; i < 128; i++)
        {
            sb.Append("{\"nested\":");
        }
        sb.Append("\"bottom\"");
        for (int i = 0; i < 128; i++)
        {
            sb.Append('}');
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var serializer = new NativeAotJsonSerializer(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());

        // Act & Assert: Should trigger MaxDepth JsonException safely without crashing the CLR stack
        Action act = () => serializer.Deserialize<SecurityTestMessage>(bytes);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Security_ExtremelyLargePayload_ProcessedOrRejectedGracefully()
    {
        // Arrange: 1 MB string payload
        string largeString = new string('A', 1024 * 1024);
        var msg = new SecurityTestMessage(largeString);
        var serializer = new NativeAotJsonSerializer(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());

        // Act
        var bytes = serializer.Serialize(msg);
        var deserialized = serializer.Deserialize<SecurityTestMessage>(bytes);

        // Assert
        deserialized.Data.Length.Should().Be(1024 * 1024);
    }

    [Fact]
    public async Task Security_UnknownDestination_ReturnsFailureWithoutException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        services.AddMessaging();
        var sp = services.BuildServiceProvider();

        var dispatcher = sp.GetRequiredService<IMessageDispatcher>();
        var payload = Encoding.UTF8.GetBytes("{\"Data\":\"hello\"}");
        var metadata = TransportMessageMetadata.Create("unknown.malicious.destination.v666");

        // Act
        var result = await dispatcher.DispatchAsync(
            "unknown.malicious.destination.v666",
            payload,
            metadata,
            sp,
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
    }
}
