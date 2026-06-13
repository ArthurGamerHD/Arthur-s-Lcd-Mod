using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LcdModCodeGenerator;

[Generator]
public sealed class StyleProperties : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidateTypes = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsCandidate(node),
                static (syntaxContext, _) => GetCandidateType(syntaxContext))
            .Where(static symbol => !ReferenceEquals(symbol, null));

        context.RegisterSourceOutput(candidateTypes.Collect(), static (spc, symbols) =>
        {
            foreach (var symbol in GetDistinctTypes(symbols))
            {
                if (!IsPartial(symbol))
                    continue;

                var properties = GetStyleProperties(symbol);
                if (properties.Count == 0)
                    continue;

                spc.AddSource(GetHintName(symbol), BuildSource(symbol, properties));
            }
        });
    }

    static bool IsCandidate(SyntaxNode node)
    {
        var classDeclaration = node as ClassDeclarationSyntax;
        if (classDeclaration == null)
            return false;

        return classDeclaration.Members
            .OfType<FieldDeclarationSyntax>()
            .Any(field => field.Declaration.Variables.Any(v =>
                v.Identifier.ValueText.EndsWith("Property", StringComparison.Ordinal)));
    }

    static INamedTypeSymbol GetCandidateType(GeneratorSyntaxContext context)
    {
        var classDeclaration = context.Node as ClassDeclarationSyntax;
        if (classDeclaration == null)
            return null;

        return context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
    }

    static IReadOnlyList<INamedTypeSymbol> GetDistinctTypes(ImmutableArray<INamedTypeSymbol> symbols)
    {
        var list = new List<INamedTypeSymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var symbol in symbols.OfType<INamedTypeSymbol>())
        {
            if (symbol.TypeKind != TypeKind.Class)
                continue;

            var key = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (seen.Add(key))
                list.Add(symbol);
        }

        return list;
    }

    static IReadOnlyList<StylePropertyDefinition> GetStyleProperties(INamedTypeSymbol symbol)
    {
        var list = new List<StylePropertyDefinition>();
        foreach (var field in symbol.GetMembers().OfType<IFieldSymbol>())
        {
            if (!field.IsStatic || field.Type == null || !field.Name.EndsWith("Property", StringComparison.Ordinal))
                continue;

            var namedType = field.Type as INamedTypeSymbol;
            if (namedType == null || namedType.TypeArguments.Length != 1)
                continue;

            if (namedType.Name != "StyleProperty" ||
                namedType.ContainingNamespace == null ||
                namedType.ContainingNamespace.ToDisplayString() != "LcdMod.Client.Gui.Styling")
            {
                continue;
            }

            var propertyName = field.Name.Substring(0, field.Name.Length - "Property".Length);
            if (string.IsNullOrEmpty(propertyName))
                continue;

            list.Add(new StylePropertyDefinition(
                field.Name,
                propertyName,
                namedType.TypeArguments[0],
                HasInheritedMember(symbol.BaseType, propertyName)));
        }

        list.Sort((a, b) => string.CompareOrdinal(a.PropertyName, b.PropertyName));
        return list;
    }

    static bool HasInheritedMember(INamedTypeSymbol baseType, string name)
    {
        for (var type = baseType; type != null; type = type.BaseType)
        {
            if (type.GetMembers(name).Length > 0)
                return true;

            foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
            {
                if (!field.IsStatic || !field.Name.EndsWith("Property", StringComparison.Ordinal))
                    continue;

                var propertyName = field.Name.Substring(0, field.Name.Length - "Property".Length);
                if (propertyName == name)
                    return true;
            }
        }

        return false;
    }

    static bool IsPartial(INamedTypeSymbol symbol)
    {
        foreach (var syntaxReference in symbol.DeclaringSyntaxReferences)
        {
            var declaration = syntaxReference.GetSyntax() as ClassDeclarationSyntax;
            if (declaration == null)
                continue;

            if (declaration.Modifiers.Any(m => m.ValueText == "partial"))
                return true;
        }

        return false;
    }

    static string BuildSource(INamedTypeSymbol symbol, IReadOnlyList<StylePropertyDefinition> properties)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("namespace " + symbol.ContainingNamespace.ToDisplayString());
        sb.AppendLine("{");
        sb.Append("    ");
        AppendTypeDeclaration(sb, symbol);
        sb.AppendLine();
        sb.AppendLine("    {");

        foreach (var property in properties)
            AppendProperty(sb, property);

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static void AppendTypeDeclaration(StringBuilder sb, INamedTypeSymbol symbol)
    {
        if (symbol.DeclaredAccessibility == Accessibility.Public)
            sb.Append("public ");
        else if (symbol.DeclaredAccessibility == Accessibility.Internal)
            sb.Append("internal ");

        if (symbol.IsAbstract)
            sb.Append("abstract ");
        if (symbol.IsSealed)
            sb.Append("sealed ");

        sb.Append("partial class ");
        sb.Append(symbol.Name);

        if (symbol.TypeParameters.Length > 0)
        {
            sb.Append("<");
            sb.Append(string.Join(", ", symbol.TypeParameters.Select(p => p.Name)));
            sb.Append(">");
        }
    }

    static void AppendProperty(StringBuilder sb, StylePropertyDefinition property)
    {
        var typeName = property.ValueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var fieldName = "_" + char.ToLowerInvariant(property.PropertyName[0]) + property.PropertyName.Substring(1) + "Value";
        var propertyModifier = property.HidesInheritedMember ? "new " : string.Empty;

        sb.AppendLine("        readonly global::LcdMod.Client.Gui.Styling.PropertyValue<" + typeName + "> " + fieldName + " = new global::LcdMod.Client.Gui.Styling.PropertyValue<" + typeName + ">();");
        sb.AppendLine();
        sb.AppendLine("        public " + propertyModifier + typeName + " " + property.PropertyName);
        sb.AppendLine("        {");
        sb.AppendLine("            get { return GetStyleValue(" + property.FieldName + ", " + fieldName + "); }");
        sb.AppendLine("            set");
        sb.AppendLine("            {");
        sb.AppendLine("                if (" + fieldName + ".LocalOverride && global::System.Collections.Generic.EqualityComparer<" + typeName + ">.Default.Equals(" + fieldName + ".Local, value))");
        sb.AppendLine("                    return;");
        sb.AppendLine();
        sb.AppendLine("                " + fieldName + ".Local = value;");
        sb.AppendLine("                " + fieldName + ".LocalOverride = true;");
        sb.AppendLine("                " + fieldName + ".HasCache = false;");
        sb.AppendLine("                MarkDirty();");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public " + propertyModifier + "bool HasLocal" + property.PropertyName);
        sb.AppendLine("        {");
        sb.AppendLine("            get { return " + fieldName + ".LocalOverride; }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public " + propertyModifier + "void Clear" + property.PropertyName + "()");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!" + fieldName + ".LocalOverride)");
        sb.AppendLine("                return;");
        sb.AppendLine();
        sb.AppendLine("            " + fieldName + ".ClearLocal();");
        sb.AppendLine("            MarkDirty();");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    static string GetHintName(INamedTypeSymbol symbol)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty)
            .Replace(".", "_")
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(",", "_")
            .Replace(" ", string.Empty) + ".StyleProperties.g.cs";
    }

    readonly struct StylePropertyDefinition
    {
        public readonly string FieldName;
        public readonly string PropertyName;
        public readonly ITypeSymbol ValueType;
        public readonly bool HidesInheritedMember;

        public StylePropertyDefinition(string fieldName, string propertyName, ITypeSymbol valueType, bool hidesInheritedMember)
        {
            FieldName = fieldName;
            PropertyName = propertyName;
            ValueType = valueType;
            HidesInheritedMember = hidesInheritedMember;
        }
    }
}
