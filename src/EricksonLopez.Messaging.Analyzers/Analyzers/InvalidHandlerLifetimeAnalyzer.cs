// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Immutable;
using System.Linq;
using EricksonLopez.Messaging.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EricksonLopez.Messaging.Analyzers.Analyzers;

/// <summary>
/// Provides a Roslyn analyzer that enforces scoped service lifetime registrations for message handlers.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InvalidHandlerLifetimeAnalyzer : DiagnosticAnalyzer
{
    private const string HandlerInterfaceName = "EricksonLopez.Messaging.Contracts.IMessageHandler";

    private static readonly string[] ForbiddenMethodNames =
    [
        "AddSingleton",
        "AddTransient",
        "TryAddSingleton",
        "TryAddTransient"
    ];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.InvalidHandlerLifetime);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        string methodName;
        GenericNameSyntax? genericName = null;

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            methodName = memberAccess.Name.Identifier.Text;
            genericName = memberAccess.Name as GenericNameSyntax;
        }
        else if (invocation.Expression is GenericNameSyntax directGeneric)
        {
            methodName = directGeneric.Identifier.Text;
            genericName = directGeneric;
        }
        else if (invocation.Expression is SimpleNameSyntax simpleName)
        {
            methodName = simpleName.Identifier.Text;
        }
        else
        {
            return;
        }

        if (!ForbiddenMethodNames.Contains(methodName, StringComparer.Ordinal))
        {
            return;
        }

        // Check generic arguments (e.g. services.AddSingleton<MyHandler>() or services.AddSingleton<IMessageHandler<T>, MyHandler>())
        if (genericName is not null)
        {
            foreach (var typeArg in genericName.TypeArgumentList.Arguments)
            {
                var typeInfo = context.SemanticModel.GetTypeInfo(typeArg);
                if (IsHandlerType(typeInfo.Type))
                {
                    var diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.InvalidHandlerLifetime,
                        typeArg.GetLocation(),
                        typeInfo.Type!.Name);

                    context.ReportDiagnostic(diagnostic);
                    return;
                }
            }
        }

        // Check typeof arguments (e.g. services.AddSingleton(typeof(MyHandler)))
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.Expression is TypeOfExpressionSyntax typeOfExpr)
            {
                var typeInfo = context.SemanticModel.GetTypeInfo(typeOfExpr.Type);
                if (IsHandlerType(typeInfo.Type))
                {
                    var diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.InvalidHandlerLifetime,
                        typeOfExpr.GetLocation(),
                        typeInfo.Type!.Name);

                    context.ReportDiagnostic(diagnostic);
                    return;
                }
            }
        }
    }

    private static bool IsHandlerType(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is null)
        {
            return false;
        }

        if (typeSymbol is INamedTypeSymbol named &&
            named.IsGenericType &&
            named.ConstructedFrom.ToDisplayString().StartsWith(HandlerInterfaceName, StringComparison.Ordinal))
        {
            return true;
        }

        return typeSymbol.AllInterfaces.Any(i =>
            i.IsGenericType &&
            i.ConstructedFrom.ToDisplayString().StartsWith(HandlerInterfaceName, StringComparison.Ordinal));
    }
}
