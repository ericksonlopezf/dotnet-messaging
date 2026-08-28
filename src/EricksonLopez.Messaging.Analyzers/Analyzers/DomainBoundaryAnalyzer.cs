// Copyright © Erickson Lopez. MIT License.
using System;
using System.Linq;

namespace EricksonLopez.Messaging.Analyzers.Analyzers;

using System.Collections.Immutable;
using EricksonLopez.Messaging.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Provides a Roslyn analyzer that ensures message contracts do not reference domain entities or aggregate roots.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DomainBoundaryAnalyzer : DiagnosticAnalyzer
{
    private const string MessageInterfaceName = "EricksonLopez.Messaging.Contracts.IMessage";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.DomainEntityInMessage);

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

        bool isMessage = namedType.AllInterfaces.Any(i =>
            i.ToDisplayString().Equals(MessageInterfaceName, StringComparison.Ordinal));

        if (!isMessage)
        {
            return;
        }

        foreach (var member in namedType.GetMembers())
        {
            if (member is IPropertySymbol property)
            {
                var propTypeName = property.Type.Name;
                var propNamespace = property.Type.ContainingNamespace?.ToDisplayString();

                bool isDomainEntity = (propNamespace is not null && propNamespace.Contains(".Domain")) ||
                                     propTypeName.EndsWith("Aggregate", StringComparison.Ordinal) ||
                                     propTypeName.EndsWith("Entity", StringComparison.Ordinal) ||
                                     propTypeName.EndsWith("AggregateRoot", StringComparison.Ordinal);

                if (isDomainEntity)
                {
                    var diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.DomainEntityInMessage,
                        property.Locations[0],
                        namedType.Name,
                        property.Name,
                        property.Type.Name);

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }
}



