// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Testing;

namespace EricksonLopez.Messaging.AotSmokeTest;

internal static class Program
{
    private static int _passedTests;

    private static void Assert([DoesNotReturnIf(false)] bool condition, string testName)
    {
        if (!condition)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] {testName}");
            Console.ResetColor();
            Environment.Exit(1);
        }
        _passedTests++;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[PASS] {testName}");
        Console.ResetColor();
    }

    public static async Task Main()
    {
        Console.WriteLine("=================================================");
        Console.WriteLine(" EricksonLopez.Messaging NativeAOT Suite         ");
        Console.WriteLine("=================================================");

        // ── 1. MessageEnvelope & Metadata Invariants ───────────────────────────
        Console.WriteLine("\n--- 1. MessageEnvelope & Metadata ---");

        var payload = new TestOrderCreated(Guid.NewGuid(), "ORD-12345", 99.99m);
        var envelope = MessageEnvelope<TestOrderCreated>.Create(
            payload: payload,
            messageType: "orders.created",
            correlationId: "corr-100",
            tenantId: "tenant-beta");

        Assert(envelope.Payload.OrderNumber == "ORD-12345", "Envelope payload matches");
        Assert(envelope.Metadata.MessageType == "orders.created", "Envelope message type matches");
        Assert(envelope.Metadata.CorrelationId == "corr-100", "Envelope correlationId matches");
        Assert(envelope.Metadata.TenantId == "tenant-beta", "Envelope tenantId matches");

        // ── 2. InMemoryTestHarness Transport ───────────────────────────────────
        Console.WriteLine("\n--- 2. InMemoryTestHarness ---");

        var harness = new InMemoryTestHarness();
        var rawBytes = Encoding.UTF8.GetBytes("{\"test\":true}");
        var metadata = TransportMessageMetadata.Create("test.event", correlationId: "corr-200");

        var result = await harness.PublishRawAsync("orders-topic", rawBytes, metadata);
        Assert(result.IsSuccess, "PublishRawAsync succeeded");
        Assert(harness.PublishedMessages.Count == 1, "harness recorded 1 published message");

        Console.WriteLine("\n=================================================");
        Console.WriteLine($" ALL {_passedTests} NATIVE AOT SUITE TESTS PASSED SUCCESSFULLY! ");
        Console.WriteLine("=== AOT Validator: OK ===");
        Console.WriteLine("=================================================");
    }
}
