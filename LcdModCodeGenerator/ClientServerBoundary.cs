using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LcdModCodeGenerator;

[Generator]
public sealed class ClientServerBoundary : IIncrementalGenerator
{
    static readonly DiagnosticDescriptor CrossBoundaryUsingWarning = new(
        id: "LcdMOD004",
        title: "Client and server code must not import each other",
        messageFormat: "{0} code must not import {1} namespace '{2}'",
        category: "LcdModCodeGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    enum SourceSide
    {
        None,
        Client,
        Server,
        Common,
        Mixed
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var input = context.CompilationProvider.Combine(context.AnalyzerConfigOptionsProvider);

        context.RegisterSourceOutput(input, static (spc, source) =>
        {
            var compilation = source.Left;
            var rootNamespace = GetRootNamespace(source.Right, compilation);
            if (string.IsNullOrEmpty(rootNamespace))
                return;

            foreach (var syntaxTree in compilation.SyntaxTrees)
                AnalyzeTree(spc, syntaxTree, rootNamespace);
        });
    }

    static void AnalyzeTree(SourceProductionContext spc, SyntaxTree syntaxTree, string rootNamespace)
    {
        var root = syntaxTree.GetRoot();
        var side = GetDeclaredSide(root, rootNamespace);
        if (side != SourceSide.Client && side != SourceSide.Server && side != SourceSide.Common)
            return;

        foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            if (usingDirective.Name == null)
                continue;

            var importedNamespace = usingDirective.Name.ToString();
            SourceSide forbiddenSide;
            if (!TryGetForbiddenSide(side, importedNamespace, rootNamespace, out forbiddenSide))
                continue;

            spc.ReportDiagnostic(Diagnostic.Create(
                CrossBoundaryUsingWarning,
                usingDirective.Name.GetLocation(),
                SideName(side),
                SideName(forbiddenSide).ToLowerInvariant(),
                importedNamespace));
        }
    }

    static string SideName(SourceSide side)
    {
        if (side == SourceSide.Client)
            return "Client";

        if (side == SourceSide.Server)
            return "Server";

        return "Common";
    }

    static bool TryGetForbiddenSide(
        SourceSide currentSide,
        string importedNamespace,
        string rootNamespace,
        out SourceSide forbiddenSide)
    {
        forbiddenSide = SourceSide.None;

        if (currentSide == SourceSide.Client &&
            IsForbiddenNamespace(importedNamespace, rootNamespace + ".Server"))
        {
            forbiddenSide = SourceSide.Server;
            return true;
        }

        if (currentSide == SourceSide.Server &&
            IsForbiddenNamespace(importedNamespace, rootNamespace + ".Client"))
        {
            forbiddenSide = SourceSide.Client;
            return true;
        }

        if (currentSide == SourceSide.Common)
        {
            if (IsForbiddenNamespace(importedNamespace, rootNamespace + ".Client"))
            {
                forbiddenSide = SourceSide.Client;
                return true;
            }

            if (IsForbiddenNamespace(importedNamespace, rootNamespace + ".Server"))
            {
                forbiddenSide = SourceSide.Server;
                return true;
            }
        }

        return false;
    }

    static SourceSide GetDeclaredSide(SyntaxNode root, string rootNamespace)
    {
        var side = SourceSide.None;

        foreach (var declaration in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
        {
            var namespaceName = declaration.Name.ToString();
            var declaredSide = GetNamespaceSide(namespaceName, rootNamespace);
            if (declaredSide == SourceSide.None)
                continue;

            if (side == SourceSide.None)
                side = declaredSide;
            else if (side != declaredSide)
                return SourceSide.Mixed;
        }

        return side;
    }

    static SourceSide GetNamespaceSide(string namespaceName, string rootNamespace)
    {
        if (IsForbiddenNamespace(namespaceName, rootNamespace + ".Client"))
            return SourceSide.Client;

        if (IsForbiddenNamespace(namespaceName, rootNamespace + ".Server"))
            return SourceSide.Server;

        if (IsForbiddenNamespace(namespaceName, rootNamespace + ".Common"))
            return SourceSide.Common;

        return SourceSide.None;
    }

    static string GetRootNamespace(AnalyzerConfigOptionsProvider optionsProvider, Compilation compilation)
    {
        string rootNamespace;
        if (optionsProvider.GlobalOptions.TryGetValue("build_property.RootNamespace", out rootNamespace) &&
            !string.IsNullOrWhiteSpace(rootNamespace))
        {
            return rootNamespace.Trim();
        }

        return InferRootNamespace(compilation);
    }

    static string InferRootNamespace(Compilation compilation)
    {
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var root = syntaxTree.GetRoot();
            foreach (var declaration in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
            {
                var namespaceName = declaration.Name.ToString();
                var marker = namespaceName.IndexOf(".Client", StringComparison.Ordinal);
                if (marker > 0 && IsBoundaryMarker(namespaceName, marker, ".Client"))
                    return namespaceName.Substring(0, marker);

                marker = namespaceName.IndexOf(".Server", StringComparison.Ordinal);
                if (marker > 0 && IsBoundaryMarker(namespaceName, marker, ".Server"))
                    return namespaceName.Substring(0, marker);

                marker = namespaceName.IndexOf(".Common", StringComparison.Ordinal);
                if (marker > 0 && IsBoundaryMarker(namespaceName, marker, ".Common"))
                    return namespaceName.Substring(0, marker);
            }
        }

        return null;
    }

    static bool IsBoundaryMarker(string namespaceName, int index, string marker)
    {
        var end = index + marker.Length;
        return end == namespaceName.Length || namespaceName[end] == '.';
    }

    static bool IsForbiddenNamespace(string namespaceName, string prefix)
    {
        return namespaceName == prefix ||
               namespaceName.StartsWith(prefix + ".", StringComparison.Ordinal);
    }
}
