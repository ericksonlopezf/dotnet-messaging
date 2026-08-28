// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Analyzers.Rules;

using Microsoft.CodeAnalysis;

/// <summary>
/// Provides diagnostic descriptors for Roslyn analyzer rules in the messaging framework.
/// </summary>
public static class DiagnosticDescriptors
{
    private const string ArchitectureCategory = "Architecture";
    private const string UsageCategory = "Usage";
    private const string PerformanceCategory = "Performance";

    /// <summary>
    /// Gets the diagnostic descriptor for rule ELMSG002, indicating a message type is missing the <c>[MessageType]</c> attribute.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingMessageTypeAttribute = new(
        id: "ELMSG002",
        title: "Message Type Must Declare [MessageType] Attribute",
        messageFormat: "Type '{0}' implements IMessage but does not declare the required [MessageType] attribute",
        category: ArchitectureCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Explicit [MessageType] identifiers are required for zero-reflection Native AOT dispatch and cross-version routing.");

    /// <summary>
    /// Gets the diagnostic descriptor for rule ELMSG004, indicating a message handler has an invalid service lifetime registration.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidHandlerLifetime = new(
        id: "ELMSG004",
        title: "IMessageHandler Must Be Registered As Scoped",
        messageFormat: "Message handler '{0}' should be registered as Scoped lifetime, not Singleton or Transient",
        category: ArchitectureCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Message handlers must execute within an isolated DI scope per message.");

    /// <summary>
    /// Gets the diagnostic descriptor for rule ELMSG005, indicating a message contract exposes domain entities or aggregate roots.
    /// </summary>
    public static readonly DiagnosticDescriptor DomainEntityInMessage = new(
        id: "ELMSG005",
        title: "Message Contract Must Not Reference Domain Entities",
        messageFormat: "Message '{0}' contains property '{1}' referencing domain entity '{2}'. Message contracts must contain only primitive or DTO types.",
        category: ArchitectureCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Domain entities must remain internal to bounded contexts and never leak into distributed message contracts.");

    /// <summary>
    /// Gets the diagnostic descriptor for rule ELMSG008, indicating a message handler performs synchronous blocking operations.
    /// </summary>
    public static readonly DiagnosticDescriptor SynchronousBlockingInHandler = new(
        id: "ELMSG008",
        title: "Message Handler Must Not Block Synchronously",
        messageFormat: "Handler '{0}' synchronously blocks on an async call ({1}). Use await instead.",
        category: PerformanceCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Synchronous blocking causes thread pool starvation in high-throughput messaging pipelines.");

    /// <summary>
    /// Gets the diagnostic descriptor for rule ELMSG010, indicating a message handler does not return <c>ValueTask&lt;Result&gt;</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor HandlerMustReturnResult = new(
        id: "ELMSG010",
        title: "Handler Must Return ValueTask<Result>",
        messageFormat: "Handler method '{0}' in '{1}' must return ValueTask<Result> for functional error handling",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "EricksonLopez.Messaging requires handlers to return ValueTask of Result for explicit functional error representation.");
}






