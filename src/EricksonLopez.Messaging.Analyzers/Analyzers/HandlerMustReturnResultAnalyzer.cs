// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Immutable;
using System.Linq;
using EricksonLopez.Messaging.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EricksonLopez.Messaging.Analyzers.Analyzers;

/// <summary>
/// Provides a Roslyn analyzer that validates message handlers return <c>ValueTask&lt;Result&gt;</c> for functional error handling.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HandlerMustReturnResultAnalyzer : DiagnosticAnalyzer
{
    private const string HandlerInterfaceName = "EricksonLopez.Messaging.Contracts.IMessageHandler";
    private const string ResultTypeName = "EricksonLopez.Result.Result";
    private const string ValueTaskTypeName = "System.Threading.Tasks.ValueTask";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.HandlerMustReturnResult);

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

        bool implementsHandler = namedType.AllInterfaces.Any(i =>
            i.IsGenericType &&
            i.ConstructedFrom.ToDisplayString().StartsWith(HandlerInterfaceName, StringComparison.Ordinal));

        if (!implementsHandler)
        {
            return;
        }

        foreach (var member in namedType.GetMembers().OfType<IMethodSymbol>())
        {
            if (member.Name != "HandleAsync")
            {
                continue;
            }

            if (!ReturnsValueTaskOfResult(member.ReturnType))
            {
                var location = member.Locations.FirstOrDefault() ?? namedType.Locations[0];
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.HandlerMustReturnResult,
                    location,
                    member.Name,
                    namedType.Name);

                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static bool ReturnsValueTaskOfResult(ITypeSymbol returnType)
    {
        if (returnType is not INamedTypeSymbol namedReturn)
        {
            return false;
        }

        if (!namedReturn.IsGenericType)
        {
            return false;
        }

        var constructedFrom = namedReturn.ConstructedFrom.ToDisplayString();
        if (!constructedFrom.StartsWith(ValueTaskTypeName, StringComparison.Ordinal))
        {
            return false;
        }

        if (namedReturn.TypeArguments.Length != 1)
        {
            return false;
        }

        var typeArg = namedReturn.TypeArguments[0];
        return string.Equals(typeArg.ToDisplayString(), ResultTypeName, StringComparison.Ordinal) ||
               string.Equals(typeArg.Name, "Result", StringComparison.Ordinal);
    }
}
