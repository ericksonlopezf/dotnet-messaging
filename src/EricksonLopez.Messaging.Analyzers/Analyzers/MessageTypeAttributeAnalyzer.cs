// Copyright © Erickson Lopez. MIT License.
using System;
using System.Linq;

namespace EricksonLopez.Messaging.Analyzers.Analyzers;

using System.Collections.Immutable;
using EricksonLopez.Messaging.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Provides a Roslyn analyzer that verifies all message implementations declare the <c>[MessageType]</c> attribute.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MessageTypeAttributeAnalyzer : DiagnosticAnalyzer
{
    private const string MessageInterfaceName = "EricksonLopez.Messaging.Contracts.IMessage";
    private const string MessageTypeAttributeName = "EricksonLopez.Messaging.Attributes.MessageTypeAttribute";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.MissingMessageTypeAttribute);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var namedType = (INamedTypeSymbol)context.Symbol;

        if (namedType.TypeKind != TypeKind.Class && namedType.TypeKind != TypeKind.Struct)
        {
            return;
        }

        if (namedType.IsAbstract)
        {
            return;
        }

        bool implementsMessage = namedType.AllInterfaces.Any(i =>
            i.ToDisplayString().Equals(MessageInterfaceName, StringComparison.Ordinal));

        if (!implementsMessage)
        {
            return;
        }

        bool hasAttribute = namedType.GetAttributes().Any(a =>
            a.AttributeClass is { } attrClass &&
            string.Equals(attrClass.ToDisplayString(), MessageTypeAttributeName, StringComparison.Ordinal));

        if (!hasAttribute)
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.MissingMessageTypeAttribute,
                namedType.Locations[0],
                namedType.Name);

            context.ReportDiagnostic(diagnostic);
        }
    }
}



