// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Result;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace EricksonLopez.Messaging.Analyzers.Tests.Common;

/// <summary>
/// Centralized factory for creating CSharp compilations with standard metadata references for Analyzer tests.
/// </summary>
public static class RoslynTestCompilationFactory
{
    private static readonly MetadataReference[] StandardReferences = new[]
    {
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(IMessage).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(MessageTypeAttribute).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(EricksonLopez.Result.Result).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ValueTask).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(CancellationToken).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location)
    };

    /// <summary>
    /// Creates a compilation for the specified source code.
    /// </summary>
    public static CSharpCompilation CreateCompilation(string source, string assemblyName = "TestCompilation")
    {
        var normalizedSource = source;
        if (!normalizedSource.Contains("using EricksonLopez.Messaging.Contracts;"))
        {
            normalizedSource = "using System;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing EricksonLopez.Messaging.Contracts;\nusing EricksonLopez.Messaging.Attributes;\nusing EricksonLopez.Result;\n" + normalizedSource;
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(normalizedSource);

        return CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            StandardReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
