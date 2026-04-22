using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LcdModCodeGenerator;

[Generator]
public sealed class TerminalControls : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(postInitializationContext =>
            postInitializationContext.AddSource("IUsesTerminalControl.g.cs", UsesTerminalControlInterfaceSource));

        var candidateTypes = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsCandidate(node),
                static (syntaxContext, _) => GetCandidateType(syntaxContext))
            .Where(static symbol => !ReferenceEquals(symbol, null));

        var generationInput = context.CompilationProvider.Combine(candidateTypes.Collect());

        context.RegisterSourceOutput(
            generationInput,
            static (sourceProductionContext, input) =>
            {
                var compilation = input.Left;
                var symbols = input.Right;
                var map = BuildControlToScriptMap(compilation, symbols);
                foreach (var entry in map)
                {
                    var source = BuildControlSource(entry.Key, entry.Value);
                    sourceProductionContext.AddSource(GetHintName(entry.Key), source);
                }
            });
    }

    static bool IsCandidate(SyntaxNode node)
    {
        var classDeclaration = node as ClassDeclarationSyntax;
        return classDeclaration != null && classDeclaration.BaseList != null;
    }

    static INamedTypeSymbol GetCandidateType(GeneratorSyntaxContext context)
    {
        var classDeclaration = context.Node as ClassDeclarationSyntax;
        if (ReferenceEquals(classDeclaration, null))
            return null;

        return context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
    }

    static Dictionary<INamedTypeSymbol, SortedSet<string>> BuildControlToScriptMap(
        Compilation compilation,
        ImmutableArray<INamedTypeSymbol> symbols)
    {
        var result = new Dictionary<INamedTypeSymbol, SortedSet<string>>(SymbolEqualityComparer.Default);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var symbol in symbols.OfType<INamedTypeSymbol>())
        {
            var symbolKey = ToDisplayName(symbol);
            if (!visited.Add(symbolKey))
                continue;

            var scriptId = TryGetScriptId(symbol);
            if (string.IsNullOrEmpty(scriptId))
                continue;

            foreach (var controlType in GetHintedControls(compilation, symbol))
            {
                if (ReferenceEquals(controlType, null))
                    continue;

                SortedSet<string> scriptIds;
                if (!result.TryGetValue(controlType, out scriptIds))
                {
                    scriptIds = new SortedSet<string>(StringComparer.Ordinal);
                    result[controlType] = scriptIds;
                }

                scriptIds.Add(scriptId);
            }
        }

        return result;
    }

    static string TryGetScriptId(INamedTypeSymbol symbol)
    {
        var idField = symbol.GetMembers("ID")
            .OfType<IFieldSymbol>()
            .FirstOrDefault(field =>
                field.IsConst &&
                field.Type.SpecialType == SpecialType.System_String &&
                field.HasConstantValue);

        return idField?.ConstantValue as string;
    }

    static IEnumerable<INamedTypeSymbol> GetHintedControls(Compilation compilation, INamedTypeSymbol symbol)
    {
        var controls = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var implementedInterface in symbol.AllInterfaces)
        {
            if (implementedInterface.Arity != 1 || implementedInterface.ContainingNamespace?.ToDisplayString() != "Generated")
                continue;

            if (implementedInterface.Name == "IUsesTerminalControl")
            {
                var controlType = implementedInterface.TypeArguments[0] as INamedTypeSymbol;
                if (!ReferenceEquals(controlType, null))
                    controls.Add(controlType);
                continue;
            }

            if (implementedInterface.Name != "IUsesTerminalControlGroup")
                continue;

            var groupType = implementedInterface.TypeArguments[0] as INamedTypeSymbol;
            if (ReferenceEquals(groupType, null))
                continue;

            foreach (var expandedControl in ExpandGroupControls(groupType, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default)))
                controls.Add(expandedControl);
        }

        foreach (var control in controls)
            yield return control;
    }

    static IEnumerable<INamedTypeSymbol> ExpandGroupControls(
        INamedTypeSymbol groupType,
        HashSet<INamedTypeSymbol> visitedGroups)
    {
        if (!visitedGroups.Add(groupType))
            yield break;

        foreach (var implementedInterface in groupType.AllInterfaces)
        {
            if (implementedInterface.Arity != 1 || implementedInterface.ContainingNamespace?.ToDisplayString() != "Generated")
                continue;

            if (implementedInterface.Name == "IContainsTerminalControl")
            {
                var controlType = implementedInterface.TypeArguments[0] as INamedTypeSymbol;
                if (!ReferenceEquals(controlType, null))
                    yield return controlType;

                continue;
            }

            if (implementedInterface.Name != "IContainsTerminalControlGroup")
                continue;

            var nestedGroup = implementedInterface.TypeArguments[0] as INamedTypeSymbol;
            if (ReferenceEquals(nestedGroup, null))
                continue;

            foreach (var nestedControl in ExpandGroupControls(nestedGroup, visitedGroups))
                yield return nestedControl;
        }
    }

    static string BuildControlSource(INamedTypeSymbol controlType, SortedSet<string> scripts)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");

        var namespaceName = controlType.ContainingNamespace?.ToDisplayString();
        if (!string.IsNullOrEmpty(namespaceName) && namespaceName != "<global namespace>")
        {
            builder.Append("namespace ");
            builder.Append(namespaceName);
            builder.AppendLine();
            builder.AppendLine("{");
        }

        AppendContainingTypeDeclarations(builder, controlType, true);

        builder.AppendLine("        public override bool VisibleForScript(string script)");
        builder.AppendLine("        {");
        builder.AppendLine("            switch (script)");
        builder.AppendLine("            {");
        foreach (var script in scripts)
        {
            builder.AppendLine("                case " + EscapeLiteral(script) + ":");
        }
        if (scripts.Count > 0)
            builder.AppendLine("                    return true;");
        builder.AppendLine("                default:");
        builder.AppendLine("                    return false;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");

        AppendContainingTypeDeclarations(builder, controlType, false);

        if (!string.IsNullOrEmpty(namespaceName) && namespaceName != "<global namespace>")
            builder.AppendLine("}");

        return builder.ToString();
    }

    static void AppendContainingTypeDeclarations(StringBuilder builder, INamedTypeSymbol typeSymbol, bool open)
    {
        var containingTypes = new List<INamedTypeSymbol>();
        var current = typeSymbol;
        while (!ReferenceEquals(current, null))
        {
            containingTypes.Add(current);
            current = current.ContainingType;
        }

        if (open)
        {
            containingTypes.Reverse();
            foreach (var type in containingTypes)
            {
                builder.Append("    partial class ");
                builder.Append(type.Name);
                builder.AppendLine();
                builder.AppendLine("    {");
            }
        }
        else
        {
            foreach (var unused in containingTypes)
                builder.AppendLine("    }");
        }
    }

    static string GetHintName(INamedTypeSymbol symbol)
    {
        return ToDisplayName(symbol)
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(".", "_") + ".VisibleForScript.g.cs";
    }

    static string EscapeLiteral(string value)
    {
        if (ReferenceEquals(value, null))
            return "null";

        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    static string ToDisplayName(ISymbol symbol)
    {
        return symbol
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty);
    }

    const string UsesTerminalControlInterfaceSource = @"// <auto-generated/>
namespace Generated
{
    public interface IVisibleTerminalControl
    {
        bool VisibleForScript(string script);
    }

    public interface ITerminalControlGroup
    {
    }

    public interface IContainsTerminalControl<TControl> where TControl : IVisibleTerminalControl
    {
    }

    public interface IContainsTerminalControlGroup<TGroup> where TGroup : ITerminalControlGroup
    {
    }

    public interface IUsesTerminalControl<TControl> where TControl : IVisibleTerminalControl
    {
    }

    public interface IUsesTerminalControlGroup<TGroup> where TGroup : ITerminalControlGroup
    {
    }
}
";
}
