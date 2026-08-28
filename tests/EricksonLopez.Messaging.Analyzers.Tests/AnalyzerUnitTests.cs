// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Messaging.Analyzers.Analyzers;
using EricksonLopez.Messaging.Analyzers.Rules;
using EricksonLopez.Messaging.Analyzers.Tests.Common;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Messaging.Analyzers.Tests;

[Trait("Category", "Roslyn")]
public class AnalyzerUnitTests
{
    private static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync(DiagnosticAnalyzer analyzer, string source)
    {
        var compilation = RoslynTestCompilationFactory.CreateCompilation(source);
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static SymbolAnalysisContext CreateSymbolContext(ISymbol symbol, Action<Diagnostic>? reportDiagnostic = null)
    {
        object boxed = default(SymbolAnalysisContext);
        typeof(SymbolAnalysisContext).GetField("_symbol", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(boxed, symbol);
        typeof(SymbolAnalysisContext).GetField("_isSupportedDiagnostic", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
            .SetValue(boxed, new Func<Diagnostic, CancellationToken, bool>((_, _) => true));
        if (reportDiagnostic != null)
        {
            typeof(SymbolAnalysisContext).GetField("_reportDiagnostic", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(boxed, reportDiagnostic);
        }
        return (SymbolAnalysisContext)boxed;
    }

    private static SyntaxNodeAnalysisContext CreateSyntaxContext(SyntaxNode node, SemanticModel semanticModel, Action<Diagnostic>? reportDiagnostic = null)
    {
        object boxed = default(SyntaxNodeAnalysisContext);
        typeof(SyntaxNodeAnalysisContext).GetField("_node", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(boxed, node);
        typeof(SyntaxNodeAnalysisContext).GetField("_semanticModel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(boxed, semanticModel);
        typeof(SyntaxNodeAnalysisContext).GetField("_isSupportedDiagnostic", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
            .SetValue(boxed, new Func<Diagnostic, CancellationToken, bool>((_, _) => true));
        if (reportDiagnostic != null)
        {
            typeof(SyntaxNodeAnalysisContext).GetField("_reportDiagnostic", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(boxed, reportDiagnostic);
        }
        return (SyntaxNodeAnalysisContext)boxed;
    }

    #region DiagnosticDescriptors Tests

    [Fact]
    public void DiagnosticDescriptors_WhenAccessed_HaveExpectedStandardProperties()
    {
        DiagnosticDescriptors.MissingMessageTypeAttribute.Id.Should().Be("ELMSG002");
        DiagnosticDescriptors.MissingMessageTypeAttribute.Title.ToString().Should().Be("Message Type Must Declare [MessageType] Attribute");
        DiagnosticDescriptors.MissingMessageTypeAttribute.MessageFormat.ToString().Should().Be("Type '{0}' implements IMessage but does not declare the required [MessageType] attribute");
        DiagnosticDescriptors.MissingMessageTypeAttribute.Category.Should().Be("Architecture");
        DiagnosticDescriptors.MissingMessageTypeAttribute.DefaultSeverity.Should().Be(DiagnosticSeverity.Error);
        DiagnosticDescriptors.MissingMessageTypeAttribute.IsEnabledByDefault.Should().BeTrue();
        DiagnosticDescriptors.MissingMessageTypeAttribute.Description.ToString().Should().Be("Explicit [MessageType] identifiers are required for zero-reflection Native AOT dispatch and cross-version routing.");

        DiagnosticDescriptors.InvalidHandlerLifetime.Id.Should().Be("ELMSG004");
        DiagnosticDescriptors.InvalidHandlerLifetime.Title.ToString().Should().Be("IMessageHandler Must Be Registered As Scoped");
        DiagnosticDescriptors.InvalidHandlerLifetime.MessageFormat.ToString().Should().Be("Message handler '{0}' should be registered as Scoped lifetime, not Singleton or Transient");
        DiagnosticDescriptors.InvalidHandlerLifetime.Category.Should().Be("Architecture");
        DiagnosticDescriptors.InvalidHandlerLifetime.DefaultSeverity.Should().Be(DiagnosticSeverity.Error);
        DiagnosticDescriptors.InvalidHandlerLifetime.IsEnabledByDefault.Should().BeTrue();
        DiagnosticDescriptors.InvalidHandlerLifetime.Description.ToString().Should().Be("Message handlers must execute within an isolated DI scope per message.");

        DiagnosticDescriptors.DomainEntityInMessage.Id.Should().Be("ELMSG005");
        DiagnosticDescriptors.DomainEntityInMessage.Title.ToString().Should().Be("Message Contract Must Not Reference Domain Entities");
        DiagnosticDescriptors.DomainEntityInMessage.MessageFormat.ToString().Should().Be("Message '{0}' contains property '{1}' referencing domain entity '{2}'. Message contracts must contain only primitive or DTO types.");
        DiagnosticDescriptors.DomainEntityInMessage.Category.Should().Be("Architecture");
        DiagnosticDescriptors.DomainEntityInMessage.DefaultSeverity.Should().Be(DiagnosticSeverity.Error);
        DiagnosticDescriptors.DomainEntityInMessage.IsEnabledByDefault.Should().BeTrue();
        DiagnosticDescriptors.DomainEntityInMessage.Description.ToString().Should().Be("Domain entities must remain internal to bounded contexts and never leak into distributed message contracts.");

        DiagnosticDescriptors.SynchronousBlockingInHandler.Id.Should().Be("ELMSG008");
        DiagnosticDescriptors.SynchronousBlockingInHandler.Title.ToString().Should().Be("Message Handler Must Not Block Synchronously");
        DiagnosticDescriptors.SynchronousBlockingInHandler.MessageFormat.ToString().Should().Be("Handler '{0}' synchronously blocks on an async call ({1}). Use await instead.");
        DiagnosticDescriptors.SynchronousBlockingInHandler.Category.Should().Be("Performance");
        DiagnosticDescriptors.SynchronousBlockingInHandler.DefaultSeverity.Should().Be(DiagnosticSeverity.Error);
        DiagnosticDescriptors.SynchronousBlockingInHandler.IsEnabledByDefault.Should().BeTrue();
        DiagnosticDescriptors.SynchronousBlockingInHandler.Description.ToString().Should().Be("Synchronous blocking causes thread pool starvation in high-throughput messaging pipelines.");

        DiagnosticDescriptors.HandlerMustReturnResult.Id.Should().Be("ELMSG010");
        DiagnosticDescriptors.HandlerMustReturnResult.Title.ToString().Should().Be("Handler Must Return ValueTask<Result>");
        DiagnosticDescriptors.HandlerMustReturnResult.MessageFormat.ToString().Should().Be("Handler method '{0}' in '{1}' must return ValueTask<Result> for functional error handling");
        DiagnosticDescriptors.HandlerMustReturnResult.Category.Should().Be("Usage");
        DiagnosticDescriptors.HandlerMustReturnResult.DefaultSeverity.Should().Be(DiagnosticSeverity.Error);
        DiagnosticDescriptors.HandlerMustReturnResult.IsEnabledByDefault.Should().BeTrue();
        DiagnosticDescriptors.HandlerMustReturnResult.Description.ToString().Should().Be("EricksonLopez.Messaging requires handlers to return ValueTask of Result for explicit functional error representation.");
    }

    #endregion

    #region Initialize Tests

    [Fact]
    public void MessageTypeAttributeAnalyzer_Initialize_ConfiguresAnalysisContext()
    {
        var analyzer = new MessageTypeAttributeAnalyzer();
        var context = Substitute.For<AnalysisContext>();

        analyzer.Initialize(context);

        context.Received(1).ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.Received(1).EnableConcurrentExecution();
        context.Received(1).RegisterSymbolAction(Arg.Any<Action<SymbolAnalysisContext>>(), Arg.Is<ImmutableArray<SymbolKind>>(k => k.Contains(SymbolKind.NamedType)));
    }

    [Fact]
    public void DomainBoundaryAnalyzer_Initialize_ConfiguresAnalysisContext()
    {
        var analyzer = new DomainBoundaryAnalyzer();
        var context = Substitute.For<AnalysisContext>();

        analyzer.Initialize(context);

        context.Received(1).ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.Received(1).EnableConcurrentExecution();
        context.Received(1).RegisterSymbolAction(Arg.Any<Action<SymbolAnalysisContext>>(), Arg.Is<ImmutableArray<SymbolKind>>(k => k.Contains(SymbolKind.NamedType)));
    }

    [Fact]
    public void HandlerAsyncAnalyzer_Initialize_ConfiguresAnalysisContext()
    {
        var analyzer = new HandlerAsyncAnalyzer();
        var context = Substitute.For<AnalysisContext>();

        analyzer.Initialize(context);

        context.Received(1).ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.Received(1).EnableConcurrentExecution();
        context.Received(1).RegisterSyntaxNodeAction(Arg.Any<Action<SyntaxNodeAnalysisContext>>(), Arg.Is<ImmutableArray<SyntaxKind>>(k => k.Contains(SyntaxKind.SimpleMemberAccessExpression)));
        context.Received(1).RegisterSyntaxNodeAction(Arg.Any<Action<SyntaxNodeAnalysisContext>>(), Arg.Is<ImmutableArray<SyntaxKind>>(k => k.Contains(SyntaxKind.InvocationExpression)));
    }

    [Fact]
    public void HandlerMustReturnResultAnalyzer_Initialize_ConfiguresAnalysisContext()
    {
        var analyzer = new HandlerMustReturnResultAnalyzer();
        var context = Substitute.For<AnalysisContext>();

        analyzer.Initialize(context);

        context.Received(1).ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.Received(1).EnableConcurrentExecution();
        context.Received(1).RegisterSymbolAction(Arg.Any<Action<SymbolAnalysisContext>>(), Arg.Is<ImmutableArray<SymbolKind>>(k => k.Contains(SymbolKind.NamedType)));
    }

    [Fact]
    public void InvalidHandlerLifetimeAnalyzer_Initialize_ConfiguresAnalysisContext()
    {
        var analyzer = new InvalidHandlerLifetimeAnalyzer();
        var context = Substitute.For<AnalysisContext>();

        analyzer.Initialize(context);

        context.Received(1).ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.Received(1).EnableConcurrentExecution();
        context.Received(1).RegisterSyntaxNodeAction(Arg.Any<Action<SyntaxNodeAnalysisContext>>(), Arg.Is<ImmutableArray<SyntaxKind>>(k => k.Contains(SyntaxKind.InvocationExpression)));
    }

    #endregion

    #region MessageTypeAttributeAnalyzer Tests (ELMSG002)

    [Fact]
    public void MessageTypeAttributeAnalyzer_SupportedDiagnostics_ContainsELMSG002()
    {
        var analyzer = new MessageTypeAttributeAnalyzer();
        analyzer.SupportedDiagnostics.Should().ContainSingle()
            .Which.Id.Should().Be("ELMSG002");
    }

    [Fact]
    public async Task MessageTypeAttributeAnalyzer_MissingAttribute_EmitsELMSG002()
    {
        const string testCode = """
            namespace Sample;

            public sealed class MissingAttrMessage : IMessage
            {
                public string Name { get; set; }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new MessageTypeAttributeAnalyzer(), testCode);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("ELMSG002");
        diagnostics[0].GetMessage().Should().Contain("MissingAttrMessage");
    }

    [Fact]
    public async Task MessageTypeAttributeAnalyzer_StructMissingAttribute_EmitsELMSG002()
    {
        const string testCode = """
            namespace Sample;

            public readonly struct MissingAttrStructMessage : IMessage
            {
                public int Id { get; }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new MessageTypeAttributeAnalyzer(), testCode);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("ELMSG002");
    }

    [Fact]
    public async Task MessageTypeAttributeAnalyzer_MultipleInterfaces_MissingAttribute_EmitsELMSG002()
    {
        const string testCode = """
            namespace Sample;

            public interface ICustomInterface { }

            public sealed class MultiInterfaceMessage : ICustomInterface, IMessage
            {
                public string Value { get; set; }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new MessageTypeAttributeAnalyzer(), testCode);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("ELMSG002");
    }

    [Fact]
    public async Task MessageTypeAttributeAnalyzer_ValidWithAttribute_NoDiagnostics()
    {
        const string testCode = """
            namespace Sample;

            [MessageType("valid.message")]
            public sealed record ValidMessage(string Text) : IMessage;
            """;

        var diagnostics = await RunAnalyzerAsync(new MessageTypeAttributeAnalyzer(), testCode);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task MessageTypeAttributeAnalyzer_AbstractClassOrInterfaceOrNonMessage_NoDiagnostics()
    {
        const string testCode = """
            namespace Sample;

            public abstract class BaseAbstractMessage : IMessage
            {
                public int Version { get; set; }
            }

            public interface ICustomMessageInterface : IMessage
            {
            }

            public class RegularNonMessageClass
            {
                public string Title { get; set; }
            }

            public enum MessageEnum
            {
                None,
                Active
            }

            public delegate void MessageDelegate();
            """;

        var diagnostics = await RunAnalyzerAsync(new MessageTypeAttributeAnalyzer(), testCode);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection test")]
    public void MessageTypeAttributeAnalyzer_AnalyzeNamedType_WhenNotClassOrStruct_ReturnsEarly()
    {
        var namedType = Substitute.For<INamedTypeSymbol>();
        namedType.TypeKind.Returns(TypeKind.Interface);
        namedType.IsAbstract.Returns(false);
        var msgInterface = Substitute.For<INamedTypeSymbol>();
        msgInterface.ToDisplayString().Returns("EricksonLopez.Messaging.Contracts.IMessage");
        namedType.AllInterfaces.Returns(ImmutableArray.Create(msgInterface));
        namedType.GetAttributes().Returns(ImmutableArray<AttributeData>.Empty);
        namedType.Locations.Returns(ImmutableArray.Create(Location.None));

        var reported = new List<Diagnostic>();
        var context = CreateSymbolContext(namedType, d => reported.Add(d));

        var method = typeof(MessageTypeAttributeAnalyzer).GetMethod("AnalyzeNamedType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        method.Invoke(null, new object[] { context });

        reported.Should().BeEmpty();
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection test")]
    public void MessageTypeAttributeAnalyzer_AnalyzeNamedType_WhenAttributeClassIsNull_EmitsDiagnostic()
    {
        var namedType = Substitute.For<INamedTypeSymbol>();
        namedType.TypeKind.Returns(TypeKind.Class);
        namedType.Locations.Returns(ImmutableArray.Create(Location.None));
        var msgInterface = Substitute.For<INamedTypeSymbol>();
        msgInterface.ToDisplayString().Returns("EricksonLopez.Messaging.Contracts.IMessage");
        namedType.AllInterfaces.Returns(ImmutableArray.Create(msgInterface));

        var attrData = Substitute.For<AttributeData>();
        namedType.GetAttributes().Returns(ImmutableArray.Create(attrData));

        var reported = new List<Diagnostic>();
        var context = CreateSymbolContext(namedType, d => reported.Add(d));

        var method = typeof(MessageTypeAttributeAnalyzer).GetMethod("AnalyzeNamedType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        method.Invoke(null, new object[] { context });

        reported.Should().ContainSingle();
    }

    #endregion

    #region DomainBoundaryAnalyzer Tests (ELMSG005)

    [Fact]
    public void DomainBoundaryAnalyzer_SupportedDiagnostics_ContainsELMSG005()
    {
        var analyzer = new DomainBoundaryAnalyzer();
        analyzer.SupportedDiagnostics.Should().ContainSingle()
            .Which.Id.Should().Be("ELMSG005");
    }

    [Fact]
    public async Task DomainBoundaryAnalyzer_ExposingDomainNamespace_EmitsELMSG005()
    {
        const string testCode = """
            namespace Sample.Domain;
            public class Customer { }

            namespace Sample.Messages;
            using Sample.Domain;

            [MessageType("customer.created")]
            public sealed class CustomerCreatedMessage : IMessage
            {
                public Customer Buyer { get; set; }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new DomainBoundaryAnalyzer(), testCode);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("ELMSG005");
        diagnostics[0].GetMessage().Should().Contain("CustomerCreatedMessage");
        diagnostics[0].GetMessage().Should().Contain("Buyer");
    }

    [Theory]
    [InlineData("OrderAggregate")]
    [InlineData("OrderEntity")]
    [InlineData("OrderAggregateRoot")]
    public async Task DomainBoundaryAnalyzer_ForbiddenSuffixes_EmitsELMSG005(string typeName)
    {
        string testCode = $$"""
            namespace Sample.Models;
            public class {{typeName}} { }

            namespace Sample.Messages;
            using Sample.Models;

            [MessageType("order.event")]
            public sealed class OrderEventMessage : IMessage
            {
                public {{typeName}} Item { get; set; }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new DomainBoundaryAnalyzer(), testCode);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("ELMSG005");
    }

    [Fact]
    public async Task DomainBoundaryAnalyzer_MultipleInterfaces_WithDomainEntity_EmitsELMSG005()
    {
        const string testCode = """
            namespace Sample.Domain;
            public class ProductEntity { }

            namespace Sample.Messages;
            using Sample.Domain;

            public interface ICustomEvent { }

            [MessageType("product.updated")]
            public sealed class ProductUpdatedMessage : ICustomEvent, IMessage
            {
                public ProductEntity Product { get; set; }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new DomainBoundaryAnalyzer(), testCode);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("ELMSG005");
    }

    [Fact]
    public async Task DomainBoundaryAnalyzer_ValidPrimitivesAndGlobalNamespace_NoDiagnostics()
    {
        const string testCode = """
            public class GlobalDto { public int Id { get; set; } }

            namespace Sample.Messages;

            [MessageType("clean.event")]
            public sealed class CleanMessage : IMessage
            {
                public string Id { get; set; } = string.Empty;
                public int Quantity { get; set; }
                public DateTime Timestamp { get; set; }
                public GlobalDto GlobalItem { get; set; }

                public void SomeMethod() { }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new DomainBoundaryAnalyzer(), testCode);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task DomainBoundaryAnalyzer_NonMessageClassWithDomainProperty_NoDiagnostics()
    {
        const string testCode = """
            namespace Sample.Domain;
            public class Customer { }

            namespace Sample;
            public class NonMessageService
            {
                public Sample.Domain.Customer DomainObj { get; set; }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new DomainBoundaryAnalyzer(), testCode);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection test")]
    public void DomainBoundaryAnalyzer_AnalyzeNamedType_WhenPropertyContainingNamespaceIsNull_NoDiagnostics()
    {
        var namedType = Substitute.For<INamedTypeSymbol>();
        namedType.TypeKind.Returns(TypeKind.Class);
        var msgInterface = Substitute.For<INamedTypeSymbol>();
        msgInterface.ToDisplayString().Returns("EricksonLopez.Messaging.Contracts.IMessage");
        namedType.AllInterfaces.Returns(ImmutableArray.Create(msgInterface));

        var prop = Substitute.For<IPropertySymbol>();
        var propType = Substitute.For<ITypeSymbol>();
        propType.Name.Returns("CleanDto");
        propType.ContainingNamespace.Returns((INamespaceSymbol?)null);
        prop.Type.Returns(propType);
        prop.Name.Returns("Item");
        prop.Locations.Returns(ImmutableArray.Create(Location.None));

        namedType.GetMembers().Returns(ImmutableArray.Create<ISymbol>(prop));

        var reported = new List<Diagnostic>();
        var context = CreateSymbolContext(namedType, d => reported.Add(d));

        var method = typeof(DomainBoundaryAnalyzer).GetMethod("AnalyzeNamedType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        method.Invoke(null, new object[] { context });

        reported.Should().BeEmpty();
    }

    #endregion

    #region HandlerAsyncAnalyzer Tests (ELMSG008)

    [Fact]
    public void HandlerAsyncAnalyzer_SupportedDiagnostics_ContainsELMSG008()
    {
        var analyzer = new HandlerAsyncAnalyzer();
        analyzer.SupportedDiagnostics.Should().ContainSingle()
            .Which.Id.Should().Be("ELMSG008");
    }

    [Fact]
    public async Task HandlerAsyncAnalyzer_ResultAccessOnTask_EmitsELMSG008()
    {
        const string testCode = """
            namespace Sample;

            [MessageType("test.msg")]
            public record TestMsg : IMessage;

            public sealed class BadResultHandler : IMessageHandler<TestMsg>
            {
                public ValueTask<Result> HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken)
                {
                    var data = FetchAsync().Result;
                    return ValueTask.FromResult(Result.Success());
                }

                private Task<string> FetchAsync() => Task.FromResult("hello");
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new HandlerAsyncAnalyzer(), testCode);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("ELMSG008");
        diagnostics[0].GetMessage().Should().Contain("BadResultHandler");
        diagnostics[0].GetMessage().Should().Contain(".Result");
    }

    [Fact]
    public async Task HandlerAsyncAnalyzer_ResultAccessOnFullyQualifiedTaskAndValueTask_EmitsELMSG008()
    {
        const string testCode = """
            namespace Sample;

            [MessageType("test.msg")]
            public record TestMsg : IMessage;

            public sealed class BadFullyQualifiedResultHandler : IMessageHandler<TestMsg>
            {
                public ValueTask<Result> HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken)
                {
                    var data1 = FetchTaskAsync().Result;
                    var data2 = FetchValueTaskAsync().Result;
                    return ValueTask.FromResult(Result.Success());
                }

                private System.Threading.Tasks.Task<string> FetchTaskAsync() => System.Threading.Tasks.Task.FromResult("a");
                private System.Threading.Tasks.ValueTask<string> FetchValueTaskAsync() => System.Threading.Tasks.ValueTask.FromResult("b");
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new HandlerAsyncAnalyzer(), testCode);

        diagnostics.Should().HaveCount(2);
        diagnostics.All(d => d.Id == "ELMSG008").Should().BeTrue();
    }

    [Fact]
    public async Task HandlerAsyncAnalyzer_WaitInvocationOnTask_EmitsELMSG008()
    {
        const string testCode = """
            namespace Sample;

            [MessageType("test.msg")]
            public record TestMsg : IMessage;

            public sealed class BadWaitHandler : IMessageHandler<TestMsg>
            {
                public ValueTask<Result> HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken)
                {
                    FetchAsync().Wait();
                    return ValueTask.FromResult(Result.Success());
                }

                private Task FetchAsync() => Task.CompletedTask;
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new HandlerAsyncAnalyzer(), testCode);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("ELMSG008");
        diagnostics[0].GetMessage().Should().Contain("BadWaitHandler");
        diagnostics[0].GetMessage().Should().Contain(".Wait()");
    }

    [Fact]
    public async Task HandlerAsyncAnalyzer_WaitInvocationOnFullyQualifiedTask_EmitsELMSG008()
    {
        const string testCode = """
            namespace Sample;

            [MessageType("test.msg")]
            public record TestMsg : IMessage;

            public sealed class BadFqWaitHandler : IMessageHandler<TestMsg>
            {
                public ValueTask<Result> HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken)
                {
                    FetchAsync().Wait();
                    FetchStringAsync().Wait();
                    return ValueTask.FromResult(Result.Success());
                }

                private System.Threading.Tasks.Task FetchAsync() => System.Threading.Tasks.Task.CompletedTask;
                private System.Threading.Tasks.Task<string> FetchStringAsync() => System.Threading.Tasks.Task.FromResult("abc");
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new HandlerAsyncAnalyzer(), testCode);

        diagnostics.Should().HaveCount(2);
        diagnostics.All(d => d.Id == "ELMSG008").Should().BeTrue();
    }

    [Fact]
    public async Task HandlerAsyncAnalyzer_GetResultInvocationOnTaskAndValueTaskAwaiter_EmitsELMSG008()
    {
        const string testCode = """
            namespace Sample;

            [MessageType("test.msg")]
            public record TestMsg : IMessage;

            public sealed class BadGetResultHandler : IMessageHandler<TestMsg>
            {
                public ValueTask<Result> HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken)
                {
                    var data1 = FetchTaskAsync().GetAwaiter().GetResult();
                    var data2 = FetchValueTaskAsync().GetAwaiter().GetResult();
                    return ValueTask.FromResult(Result.Success());
                }

                private Task<string> FetchTaskAsync() => Task.FromResult("hello");
                private ValueTask<string> FetchValueTaskAsync() => ValueTask.FromResult("world");
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new HandlerAsyncAnalyzer(), testCode);

        diagnostics.Should().HaveCount(2);
        diagnostics.All(d => d.Id == "ELMSG008" && d.GetMessage().Contains(".GetResult()")).Should().BeTrue();
    }

    [Fact]
    public async Task HandlerAsyncAnalyzer_MultipleInterfaces_EmitsELMSG008()
    {
        const string testCode = """
            namespace Sample;

            public interface ICustomHandler { }

            [MessageType("test.msg")]
            public record TestMsg : IMessage;

            public sealed class MultiInterfaceHandler : ICustomHandler, IMessageHandler<TestMsg>
            {
                public ValueTask<Result> HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken)
                {
                    var r = FetchAsync().Result;
                    return ValueTask.FromResult(Result.Success());
                }

                private Task<string> FetchAsync() => Task.FromResult("data");
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new HandlerAsyncAnalyzer(), testCode);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("ELMSG008");
    }

    [Fact]
    public async Task HandlerAsyncAnalyzer_ResultOutsideHandlerOrOnNonTask_NoDiagnostics()
    {
        const string testCode = """
            namespace Sample;

            [MessageType("test.msg")]
            public record TestMsg : IMessage;

            public class CustomHolder
            {
                public string Result { get; set; } = "clean";
            }

            public class CustomWaiter
            {
                public void Wait() { }
                public string GetResult() => "clean";
            }

            public class ServiceOutsideHandler
            {
                public string CallSync()
                {
                    return Task.FromResult("abc").Result;
                }

                public void WaitSync()
                {
                    Task.CompletedTask.Wait();
                }
            }

            public sealed class CleanAsyncHandler : IMessageHandler<TestMsg>
            {
                public async ValueTask<Result> HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken)
                {
                    var resultObj = Result.Success();
                    var isSuccess = resultObj.IsSuccess;
                    var normalInvocation = DoSomething();

                    var holder = new CustomHolder();
                    var holderResult = holder.Result;

                    var waiter = new CustomWaiter();
                    waiter.Wait();
                    var waiterResult = waiter.GetResult();

                    return await ValueTask.FromResult(Result.Success());
                }

                private string DoSomething() => "ok";
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new HandlerAsyncAnalyzer(), testCode);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task HandlerAsyncAnalyzer_UnresolvedSymbols_NoDiagnostics()
    {
        const string testCode = """
            namespace Sample;

            [MessageType("test.msg")]
            public record TestMsg : IMessage;

            public sealed class UnresolvedSymbolHandler : IMessageHandler<TestMsg>
            {
                public ValueTask<Result> HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken)
                {
                    var x = unresolvedIdentifier.Result;
                    unresolvedIdentifier.Wait();
                    var y = unresolvedIdentifier.GetResult();
                    return ValueTask.FromResult(Result.Success());
                }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new HandlerAsyncAnalyzer(), testCode);

        diagnostics.Should().BeEmpty();
    }

    private sealed class NullAttributeData : AttributeData
    {
        protected override INamedTypeSymbol? CommonAttributeClass => null;
        protected override IMethodSymbol? CommonAttributeConstructor => null;
        protected override SyntaxReference? CommonApplicationSyntaxReference => null;
        protected override ImmutableArray<TypedConstant> CommonConstructorArguments => ImmutableArray<TypedConstant>.Empty;
        protected override ImmutableArray<System.Collections.Generic.KeyValuePair<string, TypedConstant>> CommonNamedArguments => ImmutableArray<System.Collections.Generic.KeyValuePair<string, TypedConstant>>.Empty;
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection test")]
    public void MessageTypeAttributeAnalyzer_WhenAttributeClassIsNull_ReportsDiagnostic()
    {
        var symbol = Substitute.For<INamedTypeSymbol>();
        symbol.TypeKind.Returns(TypeKind.Class);
        symbol.IsAbstract.Returns(false);
        symbol.Name.Returns("TestMessage");
        var iface = Substitute.For<INamedTypeSymbol>();
        iface.ToDisplayString().Returns("EricksonLopez.Messaging.Contracts.IMessage");
        symbol.AllInterfaces.Returns(ImmutableArray.Create(iface));
        symbol.GetAttributes().Returns(ImmutableArray.Create<AttributeData>(new NullAttributeData()));
        symbol.Locations.Returns(ImmutableArray.Create(Location.None));

        var reported = new List<Diagnostic>();
        var context = CreateSymbolContext(symbol, d => reported.Add(d));

        var method = typeof(MessageTypeAttributeAnalyzer).GetMethod("AnalyzeNamedType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        method.Invoke(null, new object[] { context });

        reported.Should().ContainSingle();
        reported[0].Id.Should().Be("ELMSG002");
    }

    [Fact]
    public async Task HandlerAsyncAnalyzer_NamespaceOrMethodGroupExpression_NoDiagnostics()
    {
        const string testCode = """
            namespace Sample;
            using System;
            using System.Threading.Tasks;

            [MessageType("test.msg")]
            public record TestMsg : IMessage;

            public sealed class NamespaceOrMethodGroupHandler : IMessageHandler<TestMsg>
            {
                public ValueTask<Result> HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken)
                {
                    var a = System.Threading.Result;
                    System.Threading.Wait();
                    System.Threading.GetResult();

                    return ValueTask.FromResult(Result.Success());
                }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new HandlerAsyncAnalyzer(), testCode);
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection test")]
    public void HandlerAsyncAnalyzer_GetEnclosingClassName_WhenNoClass_ReturnsDefault()
    {
        var method = typeof(HandlerAsyncAnalyzer).GetMethod("GetEnclosingClassName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var node = SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(42));

        var name = (string)method.Invoke(null, new object[] { node })!;
        name.Should().Be("Handler");
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection test")]
    public void HandlerAsyncAnalyzer_IsInsideMessageHandler_WhenNodeOutsideClass_ReturnsFalse()
    {
        var method = typeof(HandlerAsyncAnalyzer).GetMethod("IsInsideMessageHandler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var node = SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(42));
        var semanticModel = Substitute.For<SemanticModel>();
        var context = CreateSyntaxContext(node, semanticModel);

        var result = (bool)method.Invoke(null, new object[] { context })!;
        result.Should().BeFalse();
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection test")]
    public void HandlerAsyncAnalyzer_IsInsideMessageHandler_WhenSymbolNotFound_ReturnsFalse()
    {
        var tree = CSharpSyntaxTree.ParseText("public class MyHandler { public void Foo() { int x = 1; } }");
        var node = tree.GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>().First();
        var semanticModel = Substitute.For<SemanticModel>();
        var context = CreateSyntaxContext(node, semanticModel);

        var method = typeof(HandlerAsyncAnalyzer).GetMethod("IsInsideMessageHandler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var result = (bool)method.Invoke(null, new object[] { context })!;
        result.Should().BeFalse();
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection test")]
    public void HandlerAsyncAnalyzer_IsInsideMessageHandler_WhenInterfaceNotGeneric_ReturnsFalse()
    {
        var compilation = RoslynTestCompilationFactory.CreateCompilation("""
            public interface INonGenericHandler { }
            public class NonGenericClass : INonGenericHandler { public void Foo() { int x = 1; } }
            """);
        var tree = compilation.SyntaxTrees.First();
        var semanticModel = compilation.GetSemanticModel(tree);
        var node = tree.GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>().First();
        var context = CreateSyntaxContext(node, semanticModel);

        var method = typeof(HandlerAsyncAnalyzer).GetMethod("IsInsideMessageHandler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var result = (bool)method.Invoke(null, new object[] { context })!;
        result.Should().BeFalse();
    }

    #endregion

    #region HandlerMustReturnResultAnalyzer Tests (ELMSG010)

    [Fact]
    public void HandlerMustReturnResultAnalyzer_SupportedDiagnostics_ContainsELMSG010()
    {
        var analyzer = new HandlerMustReturnResultAnalyzer();
        analyzer.SupportedDiagnostics.Should().ContainSingle()
            .Which.Id.Should().Be("ELMSG010");
    }

    [Fact]
    public async Task HandlerMustReturnResultAnalyzer_ClassWithOtherGenericInterfaceAndHandleAsyncMethod_NoDiagnostics()
    {
        const string testCode = """
            namespace Sample;
            using System.Collections.Generic;

            public class OtherService : List<string>
            {
                public void HandleAsync() { }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new HandlerMustReturnResultAnalyzer(), testCode);

        diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Task<bool>", "Task.FromResult(true)")]
    [InlineData("Task<Result>", "Task.FromResult(Result.Success())")]
    [InlineData("Task", "Task.CompletedTask")]
    [InlineData("ValueTask", "ValueTask.CompletedTask")]
    [InlineData("ValueTask<string>", "ValueTask.FromResult(\"text\")")]
    [InlineData("ValueTask<(Result, int)>", "ValueTask.FromResult((Result.Success(), 1))")]
    [InlineData("void", "")]
    public async Task HandlerMustReturnResultAnalyzer_InvalidReturnTypes_EmitsELMSG010(string returnType, string returnExpr)
    {
        string body = returnType == "void" ? "" : $"return {returnExpr};";
        string testCode = $$"""
            namespace Sample;

            [MessageType("test.msg")]
            public record TestMsg : IMessage;

            public sealed class BadReturnTypeHandler : IMessageHandler<TestMsg>
            {
                public {{returnType}} HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken)
                {
                    {{body}}
                }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new HandlerMustReturnResultAnalyzer(), testCode);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("ELMSG010");
        diagnostics[0].GetMessage().Should().Contain("BadReturnTypeHandler");
        diagnostics[0].Location.GetLineSpan().StartLinePosition.Line.Should().BeGreaterThan(4);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection test")]
    public void HandlerMustReturnResultAnalyzer_AnalyzeNamedType_WhenInterfaceNotGeneric_ReturnsEarly()
    {
        var namedType = Substitute.For<INamedTypeSymbol>();
        namedType.TypeKind.Returns(TypeKind.Class);

        var handlerInterface = Substitute.For<INamedTypeSymbol>();
        handlerInterface.IsGenericType.Returns(false);
        var constructedFrom = Substitute.For<INamedTypeSymbol>();
        constructedFrom.ToDisplayString().Returns("EricksonLopez.Messaging.Contracts.IMessageHandler");
        handlerInterface.ConstructedFrom.Returns(constructedFrom);
        namedType.AllInterfaces.Returns(ImmutableArray.Create(handlerInterface));

        var reported = new List<Diagnostic>();
        var context = CreateSymbolContext(namedType, d => reported.Add(d));

        var method = typeof(HandlerMustReturnResultAnalyzer).GetMethod("AnalyzeNamedType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        method.Invoke(null, new object[] { context });

        reported.Should().BeEmpty();
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection test")]
    public void HandlerAsyncAnalyzer_IsInsideMessageHandler_WhenInterfaceMatchesPrefixButNotGeneric_ReturnsFalse()
    {
        var compilation = RoslynTestCompilationFactory.CreateCompilation("""
            namespace EricksonLopez.Messaging.Contracts;
            public interface IMessageHandler { }
            public class NonGenericHandlerClass : IMessageHandler { public void Foo() { int x = 1; } }
            """);
        var tree = compilation.SyntaxTrees.First();
        var semanticModel = compilation.GetSemanticModel(tree);
        var node = tree.GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>().First();
        var context = CreateSyntaxContext(node, semanticModel);

        var method = typeof(HandlerAsyncAnalyzer).GetMethod("IsInsideMessageHandler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var result = (bool)method.Invoke(null, new object[] { context })!;
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HandlerMustReturnResultAnalyzer_MultipleInterfaces_EmitsELMSG010()
    {
        const string testCode = """
            namespace Sample;

            public interface ICustomHandler { }

            [MessageType("test.msg")]
            public record TestMsg : IMessage;

            public sealed class MultiInterfaceBadHandler : ICustomHandler, IMessageHandler<TestMsg>
            {
                public Task HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new HandlerMustReturnResultAnalyzer(), testCode);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("ELMSG010");
    }

    [Fact]
    public async Task HandlerMustReturnResultAnalyzer_ValidValueTaskResult_NoDiagnostics()
    {
        const string testCode = """
            namespace Sample;

            [MessageType("test.msg")]
            public record TestMsg : IMessage;

            public sealed class ValidClassHandler : IMessageHandler<TestMsg>
            {
                public ValueTask<Result> HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken) =>
                    ValueTask.FromResult(Result.Success());

                public void OtherMethod() { }
            }

            public readonly struct ValidStructHandler : IMessageHandler<TestMsg>
            {
                public ValueTask<Result> HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken) =>
                    ValueTask.FromResult(Result.Success());
            }

            public class NonHandlerService
            {
                public Task<string> HandleAsync(string input, CancellationToken cancellationToken) => Task.FromResult(input);
            }

            public interface IHandlerInterface : IMessageHandler<TestMsg>
            {
            }

            public enum DummyEnum { A }
            """;

        var diagnostics = await RunAnalyzerAsync(new HandlerMustReturnResultAnalyzer(), testCode);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection test")]
    public void HandlerMustReturnResultAnalyzer_AnalyzeNamedType_WhenNotClassOrStruct_ReturnsEarly()
    {
        var namedType = Substitute.For<INamedTypeSymbol>();
        namedType.TypeKind.Returns(TypeKind.Interface);

        var reported = new List<Diagnostic>();
        var context = CreateSymbolContext(namedType, d => reported.Add(d));

        var method = typeof(HandlerMustReturnResultAnalyzer).GetMethod("AnalyzeNamedType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        method.Invoke(null, new object[] { context });

        reported.Should().BeEmpty();
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection test")]
    public void HandlerMustReturnResultAnalyzer_AnalyzeNamedType_WhenMethodHasNoLocations_FallsBackToTypeLocation()
    {
        var namedType = Substitute.For<INamedTypeSymbol>();
        namedType.TypeKind.Returns(TypeKind.Class);
        namedType.Name.Returns("SynthesizedHandler");
        var typeLocation = Location.Create("test.cs", new Microsoft.CodeAnalysis.Text.TextSpan(10, 5), new Microsoft.CodeAnalysis.Text.LinePositionSpan(new Microsoft.CodeAnalysis.Text.LinePosition(1, 0), new Microsoft.CodeAnalysis.Text.LinePosition(1, 5)));
        namedType.Locations.Returns(ImmutableArray.Create(typeLocation));

        var handlerInterface = Substitute.For<INamedTypeSymbol>();
        handlerInterface.IsGenericType.Returns(true);
        var constructedFrom = Substitute.For<INamedTypeSymbol>();
        constructedFrom.ToDisplayString().Returns("EricksonLopez.Messaging.Contracts.IMessageHandler<T>");
        handlerInterface.ConstructedFrom.Returns(constructedFrom);
        namedType.AllInterfaces.Returns(ImmutableArray.Create(handlerInterface));

        var handleMethod = Substitute.For<IMethodSymbol>();
        handleMethod.Name.Returns("HandleAsync");
        handleMethod.Locations.Returns(ImmutableArray<Location>.Empty);
        var badReturnType = Substitute.For<INamedTypeSymbol>();
        badReturnType.IsGenericType.Returns(false);
        handleMethod.ReturnType.Returns(badReturnType);

        namedType.GetMembers().Returns(ImmutableArray.Create<ISymbol>(handleMethod));

        var reported = new List<Diagnostic>();
        var context = CreateSymbolContext(namedType, d => reported.Add(d));

        var method = typeof(HandlerMustReturnResultAnalyzer).GetMethod("AnalyzeNamedType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        method.Invoke(null, new object[] { context });

        reported.Should().ContainSingle();
        reported[0].Location.Should().Be(typeLocation);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection test")]
    public void HandlerMustReturnResultAnalyzer_AnalyzeNamedType_WhenMethodHasLocation_UsesMethodLocation()
    {
        var namedType = Substitute.For<INamedTypeSymbol>();
        namedType.TypeKind.Returns(TypeKind.Class);
        namedType.Name.Returns("SynthesizedHandler");
        var typeLocation = Location.Create("test.cs", new Microsoft.CodeAnalysis.Text.TextSpan(10, 5), new Microsoft.CodeAnalysis.Text.LinePositionSpan(new Microsoft.CodeAnalysis.Text.LinePosition(1, 0), new Microsoft.CodeAnalysis.Text.LinePosition(1, 5)));
        namedType.Locations.Returns(ImmutableArray.Create(typeLocation));

        var handlerInterface = Substitute.For<INamedTypeSymbol>();
        handlerInterface.IsGenericType.Returns(true);
        var constructedFrom = Substitute.For<INamedTypeSymbol>();
        constructedFrom.ToDisplayString().Returns("EricksonLopez.Messaging.Contracts.IMessageHandler<T>");
        handlerInterface.ConstructedFrom.Returns(constructedFrom);
        namedType.AllInterfaces.Returns(ImmutableArray.Create(handlerInterface));

        var methodLocation = Location.Create("test.cs", new Microsoft.CodeAnalysis.Text.TextSpan(50, 5), new Microsoft.CodeAnalysis.Text.LinePositionSpan(new Microsoft.CodeAnalysis.Text.LinePosition(5, 0), new Microsoft.CodeAnalysis.Text.LinePosition(5, 5)));
        var handleMethod = Substitute.For<IMethodSymbol>();
        handleMethod.Name.Returns("HandleAsync");
        handleMethod.Locations.Returns(ImmutableArray.Create(methodLocation));
        var badReturnType = Substitute.For<INamedTypeSymbol>();
        badReturnType.IsGenericType.Returns(false);
        handleMethod.ReturnType.Returns(badReturnType);

        namedType.GetMembers().Returns(ImmutableArray.Create<ISymbol>(handleMethod));

        var reported = new List<Diagnostic>();
        var context = CreateSymbolContext(namedType, d => reported.Add(d));

        var method = typeof(HandlerMustReturnResultAnalyzer).GetMethod("AnalyzeNamedType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        method.Invoke(null, new object[] { context });

        reported.Should().ContainSingle();
        reported[0].Location.Should().Be(methodLocation);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection test")]
    public void HandlerMustReturnResultAnalyzer_ReturnsValueTaskOfResult_DirectInvocation_TestsAllBranches()
    {
        var method = typeof(HandlerMustReturnResultAnalyzer).GetMethod("ReturnsValueTaskOfResult", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        // Non-named type symbol (e.g. ArrayType) -> false
        var arrayType = Substitute.For<IArrayTypeSymbol>();
        ((bool)method.Invoke(null, new object[] { arrayType })!).Should().BeFalse();

        // Non-generic named type -> false
        var nonGeneric = Substitute.For<INamedTypeSymbol>();
        nonGeneric.IsGenericType.Returns(false);
        ((bool)method.Invoke(null, new object[] { nonGeneric })!).Should().BeFalse();

        // Generic type not starting with ValueTask -> false
        var genericTask = Substitute.For<INamedTypeSymbol>();
        genericTask.IsGenericType.Returns(true);
        var taskConstructed = Substitute.For<INamedTypeSymbol>();
        taskConstructed.ToDisplayString().Returns("System.Threading.Tasks.Task");
        genericTask.ConstructedFrom.Returns(taskConstructed);
        ((bool)method.Invoke(null, new object[] { genericTask })!).Should().BeFalse();

        // Generic ValueTask with arity != 1 -> false
        var genericValueTask2 = Substitute.For<INamedTypeSymbol>();
        genericValueTask2.IsGenericType.Returns(true);
        var vtConstructed = Substitute.For<INamedTypeSymbol>();
        vtConstructed.ToDisplayString().Returns("System.Threading.Tasks.ValueTask");
        genericValueTask2.ConstructedFrom.Returns(vtConstructed);
        genericValueTask2.TypeArguments.Returns(ImmutableArray.Create(Substitute.For<ITypeSymbol>(), Substitute.For<ITypeSymbol>()));
        ((bool)method.Invoke(null, new object[] { genericValueTask2 })!).Should().BeFalse();

        // Generic ValueTask with Result type having different namespace but simple name "Result" -> true
        var genericValueTaskCustomResult = Substitute.For<INamedTypeSymbol>();
        genericValueTaskCustomResult.IsGenericType.Returns(true);
        genericValueTaskCustomResult.ConstructedFrom.Returns(vtConstructed);
        var customResultType = Substitute.For<ITypeSymbol>();
        customResultType.ToDisplayString().Returns("CustomNamespace.Result");
        customResultType.Name.Returns("Result");
        genericValueTaskCustomResult.TypeArguments.Returns(ImmutableArray.Create(customResultType));
        ((bool)method.Invoke(null, new object[] { genericValueTaskCustomResult })!).Should().BeTrue();

        // Generic ValueTask with non-Result type -> false
        var genericValueTaskString = Substitute.For<INamedTypeSymbol>();
        genericValueTaskString.IsGenericType.Returns(true);
        genericValueTaskString.ConstructedFrom.Returns(vtConstructed);
        var stringType = Substitute.For<ITypeSymbol>();
        stringType.ToDisplayString().Returns("System.String");
        stringType.Name.Returns("String");
        genericValueTaskString.TypeArguments.Returns(ImmutableArray.Create(stringType));
        ((bool)method.Invoke(null, new object[] { genericValueTaskString })!).Should().BeFalse();
    }

    #endregion

    #region InvalidHandlerLifetimeAnalyzer Tests (ELMSG004)

    [Fact]
    public void InvalidHandlerLifetimeAnalyzer_SupportedDiagnostics_ContainsELMSG004()
    {
        var analyzer = new InvalidHandlerLifetimeAnalyzer();
        analyzer.SupportedDiagnostics.Should().ContainSingle()
            .Which.Id.Should().Be("ELMSG004");
    }

    [Theory]
    [InlineData("AddSingleton")]
    [InlineData("AddTransient")]
    [InlineData("TryAddSingleton")]
    [InlineData("TryAddTransient")]
    public async Task InvalidHandlerLifetimeAnalyzer_ForbiddenGenericRegistration_EmitsELMSG004(string registrationMethod)
    {
        string testCode = $$"""
            namespace Sample;
            using Microsoft.Extensions.DependencyInjection;

            [MessageType("test.msg")]
            public record TestMsg : IMessage;

            public sealed class TestHandler : IMessageHandler<TestMsg>
            {
                public ValueTask<Result> HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken) =>
                    ValueTask.FromResult(Result.Success());
            }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.{{registrationMethod}}<Sample.TestHandler>();
                }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new InvalidHandlerLifetimeAnalyzer(), testCode);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("ELMSG004");
        diagnostics[0].GetMessage().Should().Be("Message handler 'TestHandler' should be registered as Scoped lifetime, not Singleton or Transient");
    }

    [Fact]
    public async Task InvalidHandlerLifetimeAnalyzer_ForbiddenGenericRegistrationWithInterface_EmitsELMSG004()
    {
        const string testCode = """
            namespace Sample;
            using Microsoft.Extensions.DependencyInjection;

            [MessageType("test.msg")]
            public record TestMsg : IMessage;

            public sealed class TestHandler : IMessageHandler<TestMsg>
            {
                public ValueTask<Result> HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken) =>
                    ValueTask.FromResult(Result.Success());
            }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IMessageHandler<TestMsg>, TestHandler>();
                }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new InvalidHandlerLifetimeAnalyzer(), testCode);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("ELMSG004");
    }

    [Theory]
    [InlineData("AddSingleton")]
    [InlineData("AddTransient")]
    [InlineData("TryAddSingleton")]
    [InlineData("TryAddTransient")]
    public async Task InvalidHandlerLifetimeAnalyzer_ForbiddenTypeOfRegistration_EmitsELMSG004(string registrationMethod)
    {
        string testCode = $$"""
            namespace Sample;
            using Microsoft.Extensions.DependencyInjection;

            [MessageType("test.msg")]
            public record TestMsg : IMessage;

            public sealed class TestHandler : IMessageHandler<TestMsg>
            {
                public ValueTask<Result> HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken) =>
                    ValueTask.FromResult(Result.Success());
            }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.{{registrationMethod}}(typeof(Sample.TestHandler));
                }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new InvalidHandlerLifetimeAnalyzer(), testCode);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("ELMSG004");
        diagnostics[0].GetMessage().Should().Be("Message handler 'TestHandler' should be registered as Scoped lifetime, not Singleton or Transient");
    }

    [Fact]
    public async Task InvalidHandlerLifetimeAnalyzer_TypeOfWithInterfaceAndImplementation_EmitsELMSG004()
    {
        const string testCode = """
            namespace Sample;
            using Microsoft.Extensions.DependencyInjection;

            [MessageType("test.msg")]
            public record TestMsg : IMessage;

            public sealed class TestHandler : IMessageHandler<TestMsg>
            {
                public ValueTask<Result> HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken) =>
                    ValueTask.FromResult(Result.Success());
            }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IMessageHandler<TestMsg>), typeof(TestHandler));
                }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new InvalidHandlerLifetimeAnalyzer(), testCode);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("ELMSG004");
    }

    [Fact]
    public async Task InvalidHandlerLifetimeAnalyzer_MultiInterfaceHandler_EmitsELMSG004()
    {
        const string testCode = """
            namespace Sample;
            using Microsoft.Extensions.DependencyInjection;

            public interface ICustomHandler { }

            [MessageType("test.msg")]
            public record TestMsg : IMessage;

            public sealed class MultiHandler : ICustomHandler, IMessageHandler<TestMsg>
            {
                public ValueTask<Result> HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken) =>
                    ValueTask.FromResult(Result.Success());
            }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddSingleton<MultiHandler>();
                }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new InvalidHandlerLifetimeAnalyzer(), testCode);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("ELMSG004");
    }

    [Fact]
    public async Task InvalidHandlerLifetimeAnalyzer_AllowedRegistrations_NoDiagnostics()
    {
        const string testCode = """
            namespace Sample;
            using Microsoft.Extensions.DependencyInjection;

            public interface IRegularService { }
            public class RegularService : IRegularService { }

            [MessageType("test.msg")]
            public record TestMsg : IMessage;

            public sealed class TestHandler : IMessageHandler<TestMsg>
            {
                public ValueTask<Result> HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken) =>
                    ValueTask.FromResult(Result.Success());
            }

            public class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddScoped<TestHandler>();
                    services.AddScoped<IMessageHandler<TestMsg>, TestHandler>();
                    services.AddScoped(typeof(TestHandler));
                    services.AddSingleton<IRegularService, RegularService>();
                    services.AddSingleton(typeof(RegularService));
                }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new InvalidHandlerLifetimeAnalyzer(), testCode);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection test")]
    public void InvalidHandlerLifetimeAnalyzer_IsHandlerType_DirectInvocation_TestsAllBranches()
    {
        var method = typeof(InvalidHandlerLifetimeAnalyzer).GetMethod("IsHandlerType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        // null -> false
        ((bool)method.Invoke(null, new object?[] { null })!).Should().BeFalse();

        // Direct generic interface IMessageHandler<T> -> true
        var directGeneric = Substitute.For<INamedTypeSymbol>();
        directGeneric.IsGenericType.Returns(true);
        var constructed = Substitute.For<INamedTypeSymbol>();
        constructed.ToDisplayString().Returns("EricksonLopez.Messaging.Contracts.IMessageHandler<MyMessage>");
        directGeneric.ConstructedFrom.Returns(constructed);
        ((bool)method.Invoke(null, new object[] { directGeneric })!).Should().BeTrue();

        // Direct generic interface of unrelated type -> false (if AllInterfaces empty)
        var unrelatedGeneric = Substitute.For<INamedTypeSymbol>();
        unrelatedGeneric.IsGenericType.Returns(true);
        var unrelatedConstructed = Substitute.For<INamedTypeSymbol>();
        unrelatedConstructed.ToDisplayString().Returns("System.Collections.Generic.List<int>");
        unrelatedGeneric.ConstructedFrom.Returns(unrelatedConstructed);
        unrelatedGeneric.AllInterfaces.Returns(ImmutableArray<INamedTypeSymbol>.Empty);
        ((bool)method.Invoke(null, new object[] { unrelatedGeneric })!).Should().BeFalse();

        // Class with non-generic interface -> false
        var nonGenericInterface = Substitute.For<INamedTypeSymbol>();
        nonGenericInterface.IsGenericType.Returns(false);
        var classWithNonGeneric = Substitute.For<INamedTypeSymbol>();
        classWithNonGeneric.IsGenericType.Returns(false);
        classWithNonGeneric.AllInterfaces.Returns(ImmutableArray.Create(nonGenericInterface));
        ((bool)method.Invoke(null, new object[] { classWithNonGeneric })!).Should().BeFalse();
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection test")]
    public void InvalidHandlerLifetimeAnalyzer_IsHandlerType_WhenInterfaceMatchesPrefixButNotGeneric_ReturnsFalse()
    {
        var method = typeof(InvalidHandlerLifetimeAnalyzer).GetMethod("IsHandlerType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var iface = Substitute.For<INamedTypeSymbol>();
        iface.IsGenericType.Returns(false);
        var constructed = Substitute.For<INamedTypeSymbol>();
        constructed.ToDisplayString().Returns("EricksonLopez.Messaging.Contracts.IMessageHandler");
        iface.ConstructedFrom.Returns(constructed);

        var classSymbol = Substitute.For<INamedTypeSymbol>();
        classSymbol.IsGenericType.Returns(false);
        classSymbol.AllInterfaces.Returns(ImmutableArray.Create(iface));

        ((bool)method.Invoke(null, new object[] { classSymbol })!).Should().BeFalse();
    }

    [Fact]
    public async Task InvalidHandlerLifetimeAnalyzer_DirectGenericInvocation_EmitsELMSG004()
    {
        const string testCode = """
            namespace Sample;
            using Microsoft.Extensions.DependencyInjection;

            [MessageType("test.msg")]
            public record TestMsg : IMessage;

            public sealed class TestHandler : IMessageHandler<TestMsg>
            {
                public ValueTask<Result> HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken) =>
                    ValueTask.FromResult(Result.Success());
            }

            public class ServiceSetup
            {
                public static void AddSingleton<T>() { }

                public void Configure()
                {
                    AddSingleton<TestHandler>();
                }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new InvalidHandlerLifetimeAnalyzer(), testCode);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("ELMSG004");
    }

    [Fact]
    public async Task InvalidHandlerLifetimeAnalyzer_DirectSimpleInvocation_EmitsELMSG004()
    {
        const string testCode = """
            namespace Sample;
            using System;
            using Microsoft.Extensions.DependencyInjection;

            [MessageType("test.msg")]
            public record TestMsg : IMessage;

            public sealed class TestHandler : IMessageHandler<TestMsg>
            {
                public ValueTask<Result> HandleAsync(TestMsg message, MessageContext context, CancellationToken cancellationToken) =>
                    ValueTask.FromResult(Result.Success());
            }

            public class ServiceSetup
            {
                public static void AddSingleton(Type t) { }

                public void Configure()
                {
                    AddSingleton(typeof(TestHandler));
                }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new InvalidHandlerLifetimeAnalyzer(), testCode);

        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("ELMSG004");
    }

    [Fact]
    public async Task InvalidHandlerLifetimeAnalyzer_OtherInvocationExpression_NoDiagnostics()
    {
        const string testCode = """
            namespace Sample;
            using System;

            public class ServiceSetup
            {
                public void Configure(Func<int>[] actions)
                {
                    actions[0]();
                }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(new InvalidHandlerLifetimeAnalyzer(), testCode);

        diagnostics.Should().BeEmpty();
    }

    #endregion
}
