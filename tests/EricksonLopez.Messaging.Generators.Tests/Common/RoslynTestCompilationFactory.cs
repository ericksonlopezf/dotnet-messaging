// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Attributes;
using EricksonLopez.Messaging.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace EricksonLopez.Messaging.Generators.Tests.Common;

/// <summary>
/// Centralized factory for creating CSharp compilations with standard metadata references for Generator tests.
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
        MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(IEquatable<>).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Text.Json.Serialization.JsonSerializerContext).Assembly.Location)
    };

    /// <summary>
    /// Creates a compilation for the specified source code.
    /// </summary>
    public static CSharpCompilation CreateCompilation(string source, string assemblyName = "TestAssembly")
    {
        var normalizedSource = source;
        if (!normalizedSource.Contains("using EricksonLopez.Messaging.Contracts;"))
        {
            normalizedSource = "using System;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing EricksonLopez.Messaging.Contracts;\n" + normalizedSource;
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(normalizedSource);

        return CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            StandardReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
