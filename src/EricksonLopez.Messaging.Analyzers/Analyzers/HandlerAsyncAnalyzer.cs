// Copyright © Erickson Lopez. MIT License.
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Analyzers.Analyzers;

using System.Collections.Immutable;
using EricksonLopez.Messaging.Analyzers.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Provides a Roslyn analyzer that detects synchronous blocking calls in message handlers.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HandlerAsyncAnalyzer : DiagnosticAnalyzer
{
    private const string HandlerInterfaceName = "EricksonLopez.Messaging.Contracts.IMessageHandler";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.SynchronousBlockingInHandler);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        if (memberAccess.Name.Identifier.Text != "Result")
        {
            return;
        }

        if (!IsInsideMessageHandler(context))
        {
            return;
        }

        var typeName = context.SemanticModel.GetTypeInfo(memberAccess.Expression).Type?.ToDisplayString();
        if (typeName is not null &&
            (typeName.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal) ||
             typeName.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal)))
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.SynchronousBlockingInHandler,
                memberAccess.GetLocation(),
                GetEnclosingClassName(memberAccess),
                ".Result");

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        var methodName = memberAccess.Name.Identifier.Text;
        if (methodName != "Wait" && methodName != "GetResult")
        {
            return;
        }

        if (!IsInsideMessageHandler(context))
        {
            return;
        }

        var typeName = context.SemanticModel.GetTypeInfo(memberAccess.Expression).Type?.ToDisplayString();
        if (typeName is not null &&
            (typeName.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal) ||
             typeName.StartsWith("System.Runtime.CompilerServices.TaskAwaiter", StringComparison.Ordinal) ||
             typeName.StartsWith("System.Runtime.CompilerServices.ValueTaskAwaiter", StringComparison.Ordinal)))
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.SynchronousBlockingInHandler,
                invocation.GetLocation(),
                GetEnclosingClassName(invocation),
                $".{methodName}()");

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsInsideMessageHandler(SyntaxNodeAnalysisContext context)
    {
        var classDeclaration = context.Node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classDeclaration is null)
        {
            return false;
        }

        if (context.SemanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
        {
            return false;
        }

        return classSymbol.AllInterfaces.Any(i =>
            i.IsGenericType &&
            i.ConstructedFrom.ToDisplayString().StartsWith(HandlerInterfaceName, StringComparison.Ordinal));
    }

    private static string GetEnclosingClassName(SyntaxNode node)
    {
        var classDeclaration = node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        return classDeclaration?.Identifier.Text ?? "Handler";
    }
}






