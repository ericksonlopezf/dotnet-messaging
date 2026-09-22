// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;

namespace EricksonLopez.Messaging.Generators;

using System.Collections.Immutable;
using System.Linq;
using System.Text;
using EricksonLopez.Messaging.Generators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// Provides an incremental source generator that discovers message handlers at compile time and generates registration code.
/// </summary>
[Generator]
public sealed class MessagingIncrementalGenerator : IIncrementalGenerator
{
    private const string HandlerInterfaceName = "EricksonLopez.Messaging.Contracts.IMessageHandler";
    private const string MessageTypeAttributeName = "EricksonLopez.Messaging.Attributes.MessageTypeAttribute";
    private const string PartitionKeyAttributeName = "EricksonLopez.Messaging.Attributes.PartitionKeyAttribute";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<HandlerDiscoveryInfo> handlerDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsCandidateClass(s),
                transform: static (ctx, _) => GetHandlersInfo(ctx))
            .SelectMany(static (handlers, _) => handlers);

        IncrementalValueProvider<ImmutableArray<HandlerDiscoveryInfo>> collectedHandlers = handlerDeclarations.Collect();

        IncrementalValuesProvider<PartitionKeyInfo> partitionKeyDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsCandidatePartitionKeyClass(s),
                transform: static (ctx, _) => GetPartitionKeyInfo(ctx))
            .SelectMany(static (keys, _) => keys);
            
        IncrementalValueProvider<ImmutableArray<PartitionKeyInfo>> collectedPartitionKeys = partitionKeyDeclarations.Collect();

        var combined = collectedHandlers.Combine(collectedPartitionKeys);

        context.RegisterSourceOutput(combined, static (spc, source) =>
        {
            var validHandlers = source.Left.Distinct().OrderBy(h => h.HandlerFullName, StringComparer.Ordinal).ThenBy(h => h.MessageFullName, StringComparer.Ordinal).ToList();
            var validPartitionKeys = source.Right.Distinct().OrderBy(k => k.MessageFullName, StringComparer.Ordinal).ToList();
            EmitGeneratedRegistrations(spc, validHandlers, validPartitionKeys);
        });
    }

    private static bool IsCandidateClass(SyntaxNode node)
    {
        return (node is ClassDeclarationSyntax || (node is RecordDeclarationSyntax && node.IsKind(SyntaxKind.RecordDeclaration))) &&
               node is TypeDeclarationSyntax typeDecl &&
               !typeDecl.Modifiers.Any(SyntaxKind.AbstractKeyword) &&
               typeDecl.BaseList != null &&
               typeDecl.BaseList.Types.Count > 0;
    }

    private static ImmutableArray<HandlerDiscoveryInfo> GetHandlersInfo(GeneratorSyntaxContext context)
    {
        var typeDeclaration = (TypeDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol classSymbol)
        {
            return ImmutableArray<HandlerDiscoveryInfo>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<HandlerDiscoveryInfo>();

        foreach (var iface in classSymbol.AllInterfaces)
        {
            if (iface.IsGenericType &&
                iface.ConstructedFrom.ToDisplayString().StartsWith(HandlerInterfaceName, StringComparison.Ordinal))
            {
                var messageTypeSymbol = iface.TypeArguments[0];
                var messageTypeName = ResolveMessageTypeName(messageTypeSymbol);

                builder.Add(new HandlerDiscoveryInfo(
                    handlerFullName: classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    messageFullName: messageTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    messageTypeName: messageTypeName,
                    handlerName: classSymbol.Name));
            }
        }

        return builder.ToImmutable();
    }

    private static bool IsCandidatePartitionKeyClass(SyntaxNode node)
    {
        return (node is ClassDeclarationSyntax || (node is RecordDeclarationSyntax && node.IsKind(SyntaxKind.RecordDeclaration))) &&
               node is TypeDeclarationSyntax typeDecl &&
               typeDecl.Members.OfType<PropertyDeclarationSyntax>().Any(p => p.AttributeLists.Count > 0);
    }

    private static ImmutableArray<PartitionKeyInfo> GetPartitionKeyInfo(GeneratorSyntaxContext context)
    {
        var typeDeclaration = (TypeDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol classSymbol)
        {
            return ImmutableArray<PartitionKeyInfo>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<PartitionKeyInfo>();

        foreach (var member in classSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            foreach (var attr in member.GetAttributes())
            {
                if (attr.AttributeClass != null &&
                    string.Equals(attr.AttributeClass.ToDisplayString(), PartitionKeyAttributeName, StringComparison.Ordinal))
                {
                    builder.Add(new PartitionKeyInfo(
                        messageFullName: classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        propertyName: member.Name,
                        propertyType: member.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                    break;
                }
            }
        }

        return builder.ToImmutable();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed", Justification = "Internal helper tested via unit tests.")]
    internal static HandlerDiscoveryInfo? GetHandlerInfo(GeneratorSyntaxContext context)
    {
        var handlers = GetHandlersInfo(context);
        return handlers.Length > 0 ? handlers[0] : null;
    }

    private static string ResolveMessageTypeName(ITypeSymbol messageTypeSymbol)
    {
        foreach (var attr in messageTypeSymbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass != null &&
                (string.Equals(attrClass.ToDisplayString(), MessageTypeAttributeName, StringComparison.Ordinal) ||
                 string.Equals(attrClass.Name, "MessageTypeAttribute", StringComparison.Ordinal)))
            {
                if (attr.ConstructorArguments.Length > 0 &&
                    attr.ConstructorArguments[0].Value is string customName)
                {
                    return customName;
                }

                if (attr.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax attrSyntax &&
                    attrSyntax.ArgumentList?.Arguments.Count > 0 &&
                    attrSyntax.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax lit)
                {
                    return lit.Token.ValueText;
                }
            }
        }

        return messageTypeSymbol.Name;
    }

    private static void EmitGeneratedRegistrations(SourceProductionContext context, List<HandlerDiscoveryInfo> handlers, List<PartitionKeyInfo> partitionKeys)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        var messageTypes = handlers
            .Select(h => h.MessageFullName)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        sb.AppendLine("namespace EricksonLopez.Messaging.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    using System.Text.Json.Serialization;");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Compile-time source-generated JsonSerializerContext for discovered message types.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    [JsonSourceGenerationOptions(");
        sb.AppendLine("        WriteIndented = false,");
        sb.AppendLine("        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,");
        sb.AppendLine("        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,");
        sb.AppendLine("        GenerationMode = JsonSourceGenerationMode.Default)]");

        foreach (var msgType in messageTypes)
        {
            sb.AppendLine($"    [JsonSerializable(typeof({msgType}))]");
        }

        sb.AppendLine("    internal sealed partial class GeneratedMessagingJsonSerializerContext : JsonSerializerContext");
        sb.AppendLine("    {");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
        
        sb.AppendLine("namespace EricksonLopez.Messaging.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    using System;");
        sb.AppendLine("    using EricksonLopez.Messaging.Contracts;");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Compile-time source-generated IPartitionKeyResolver.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    internal sealed class GeneratedMessagingPartitionKeyResolver : IPartitionKeyResolver");
        sb.AppendLine("    {");
        sb.AppendLine("        public string? Resolve<TMessage>(TMessage message) where TMessage : notnull");
        sb.AppendLine("        {");
        
        if (partitionKeys.Count > 0)
        {
            sb.AppendLine("            switch (message)");
            sb.AppendLine("            {");
            foreach (var keyInfo in partitionKeys)
            {
                sb.AppendLine($"                case {keyInfo.MessageFullName} typedMessage{partitionKeys.IndexOf(keyInfo)}:");
                sb.AppendLine($"                    return typedMessage{partitionKeys.IndexOf(keyInfo)}.{keyInfo.PropertyName}?.ToString();");
            }
            sb.AppendLine("            }");
        }
        
        sb.AppendLine("            return null;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("namespace Microsoft.Extensions.DependencyInjection");
        sb.AppendLine("{");
        sb.AppendLine("    using System;");
        sb.AppendLine("    using EricksonLopez.Messaging.Contracts;");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Compile-time generated extensions for zero-reflection messaging handler registration.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static class GeneratedMessagingServiceCollectionExtensions");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Registers all compile-time discovered message handlers and JSON serialization context without runtime reflection.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static IServiceCollection AddGeneratedMessagingHandlers(this IServiceCollection services)");
        sb.AppendLine("        {");
        sb.AppendLine("            services.AddSingleton<System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver>(");
        sb.AppendLine("                EricksonLopez.Messaging.Generated.GeneratedMessagingJsonSerializerContext.Default);");
        sb.AppendLine();
        sb.AppendLine("            services.AddSingleton<EricksonLopez.Messaging.Contracts.IPartitionKeyResolver>(");
        sb.AppendLine("                new EricksonLopez.Messaging.Generated.GeneratedMessagingPartitionKeyResolver());");
        sb.AppendLine();

        foreach (var handler in handlers)
        {
            sb.AppendLine($"            // Handler for message '{handler.MessageTypeName}'");
            sb.AppendLine($"            services.AddMessageHandler<{handler.MessageFullName}, {handler.HandlerFullName}>();");
        }

        sb.AppendLine();
        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("GeneratedMessagingExtensions.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }
}


