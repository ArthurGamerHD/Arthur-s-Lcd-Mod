using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LcdModCodeGenerator;

[Generator]
public class ClonableContract : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(postInitializationContext =>
        {
            postInitializationContext.AddSource("IClonableContract.g.cs", CLONABLE_CONTRACT_INTERFACE_SOURCE);
            postInitializationContext.AddSource("ICloneSource.g.cs", CLONE_SOURCE_INTERFACE_SOURCE);
        });

        var clonableTypes = context.SyntaxProvider
            .CreateSyntaxProvider(
                (node, _) => IsCandidate(node),
                (syntaxContext, _) => GetTargetType(syntaxContext))
            .Where(symbol => !ReferenceEquals(symbol, null));

        context.RegisterSourceOutput(
            clonableTypes.Collect(),
            (sourceProductionContext, symbols) =>
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var symbol in symbols.OfType<INamedTypeSymbol>())
                {
                    var hintName = GetHintName(symbol);
                    if (!seen.Add(hintName))
                        continue;

                    var source = BuildSource(symbol);
                    if (ReferenceEquals(source, null))
                        continue;

                    sourceProductionContext.AddSource(hintName, source);
                }
            });
    }

    static bool IsCandidate(SyntaxNode node)
    {
        var classDeclaration = node as ClassDeclarationSyntax;
        return classDeclaration != null && classDeclaration.BaseList != null;
    }

    static INamedTypeSymbol GetTargetType(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
        if (ReferenceEquals(symbol, null))
            return null;

        if (!ImplementsClonableContractInterface(symbol))
            return null;

        if (!classDeclaration.Modifiers.Any(m => m.ValueText == "partial"))
            return null;

        return symbol;
    }

    static bool ImplementsClonableContractInterface(INamedTypeSymbol symbol)
    {
        foreach (var implementedInterface in symbol.AllInterfaces)
        {
            if (implementedInterface.Name != "IClonableContract")
                continue;

            if (implementedInterface.Arity == 0)
                return true;
        }

        return false;
    }

    static bool HasCopyFromMethod(INamedTypeSymbol symbol, ITypeSymbol parameterType)
    {
        foreach (var method in symbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.Name != "CopyFrom")
                continue;

            if (method.Parameters.Length != 1)
                continue;

            if (!SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, parameterType))
                continue;

            return true;
        }

        return false;
    }

    static bool HasCloneMethod(INamedTypeSymbol symbol)
    {
        foreach (var method in symbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.Name != "Clone")
                continue;

            if (method.Parameters.Length != 1)
                continue;

            if (method.Parameters[0].Type.SpecialType != SpecialType.System_Object)
                continue;

            return true;
        }

        return false;
    }

    static bool DirectlyImplementsClonableContract(INamedTypeSymbol symbol)
    {
        foreach (var implementedInterface in symbol.Interfaces)
        {
            if (implementedInterface.Name != "IClonableContract")
                continue;

            if (implementedInterface.Arity == 0)
                return true;
        }

        return false;
    }

    static string BuildSource(INamedTypeSymbol typeSymbol)
    {
        var clonableProperties = typeSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(IsProtoMemberProperty)
            .OrderBy(GetProtoMemberOrder)
            .ToArray();
        var compatibleInterfaces = GetCompatibleCopyInterfaces(typeSymbol, clonableProperties);
        
        var hasCloneMethod = HasCloneMethod(typeSymbol);
        var hasSelfCopyFrom = HasCopyFromMethod(typeSymbol, typeSymbol);
        var clonableTargets = new[] { typeSymbol };
        var directlyImplementsContract = DirectlyImplementsClonableContract(typeSymbol);
        var shouldGenerateClone = !hasCloneMethod && (directlyImplementsContract || !ReferenceEquals(typeSymbol.BaseType, null));
        var shouldGenerateSelfCopyFrom = !hasSelfCopyFrom && clonableProperties.Length > 0;
        var canUseSelfCopyFromInClone = hasSelfCopyFrom || shouldGenerateSelfCopyFrom;
        if (!shouldGenerateClone && !shouldGenerateSelfCopyFrom)
            return null;

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");

        var namespaceName = typeSymbol.ContainingNamespace == null
            ? string.Empty
            : typeSymbol.ContainingNamespace.ToDisplayString();
        if (!string.IsNullOrEmpty(namespaceName) && namespaceName != "<global namespace>")
        {
            builder.Append("namespace ");
            builder.Append(namespaceName);
            builder.AppendLine();
            builder.AppendLine("{");
        }

        AppendContainingTypeDeclarations(builder, typeSymbol, true);

        if (shouldGenerateClone)
        {
            if (directlyImplementsContract)
                builder.AppendLine("        public virtual void Clone(object other)");
            else
                builder.AppendLine("        public override void Clone(object other)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (global::System.Object.ReferenceEquals(other, null))");
            builder.AppendLine("                throw new global::System.ArgumentNullException(\"other\");");
            builder.AppendLine();

            if (!directlyImplementsContract)
            {
                builder.AppendLine("            base.Clone(other);");
                builder.AppendLine();
            }
            
            if (canUseSelfCopyFromInClone)
            {
                foreach (var target in clonableTargets)
                {
                    var fullTargetType = target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var safeName = target.Name.Replace("<", "_").Replace(">", "_");
                    builder.AppendLine("            var sourceAs" + safeName + " = other as " + fullTargetType + ";");
                    builder.AppendLine("            if (!global::System.Object.ReferenceEquals(sourceAs" + safeName + ", null))");
                    builder.AppendLine("            {");
                    builder.AppendLine("                CopyFrom(sourceAs" + safeName + ");");
                    builder.AppendLine("                return;");
                    builder.AppendLine("            }");
                    builder.AppendLine();
                }
            }
            else
            {
                var fullTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var safeTypeName = typeSymbol.Name.Replace("<", "_").Replace(">", "_");
                builder.AppendLine("            var sourceAs" + safeTypeName + " = other as " + fullTypeName + ";");
                builder.AppendLine("            if (!global::System.Object.ReferenceEquals(sourceAs" + safeTypeName + ", null))");
                builder.AppendLine("                return;");
                builder.AppendLine();
            }

            foreach (var compatibleInterface in compatibleInterfaces)
            {
                var fullInterfaceName = compatibleInterface.InterfaceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var safeName = GetSafeName(compatibleInterface.InterfaceType);
                builder.AppendLine("            var sourceAs" + safeName + " = other as " + fullInterfaceName + ";");
                builder.AppendLine("            if (!global::System.Object.ReferenceEquals(sourceAs" + safeName + ", null))");
                builder.AppendLine("            {");
                foreach (var property in compatibleInterface.Properties)
                    builder.AppendLine("                " + property.Name + " = sourceAs" + safeName + "." + property.Name + ";");
                builder.AppendLine("                return;");
                builder.AppendLine("            }");
                builder.AppendLine();
            }

            if (directlyImplementsContract)
                builder.AppendLine("            throw new global::System.ArgumentException(\"Unsupported clone source type\", \"other\");");
            builder.AppendLine("        }");
            builder.AppendLine();
        }

        if (shouldGenerateSelfCopyFrom)
        {
            builder.AppendLine("        private void CopyFrom(" + typeSymbol.Name + " other)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (global::System.Object.ReferenceEquals(other, null))");
            builder.AppendLine("                throw new global::System.ArgumentNullException(\"other\");");
            builder.AppendLine();

            foreach (var property in clonableProperties)
                builder.AppendLine("            " + property.Name + " = other." + property.Name + ";");

            builder.AppendLine("        }");
        }

        AppendContainingTypeDeclarations(builder, typeSymbol, false);

        if (!string.IsNullOrEmpty(namespaceName) && namespaceName != "<global namespace>")
            builder.AppendLine("}");

        return builder.ToString();
    }

    static List<(INamedTypeSymbol InterfaceType, IPropertySymbol[] Properties)> GetCompatibleCopyInterfaces(
        INamedTypeSymbol typeSymbol,
        IPropertySymbol[] clonableProperties)
    {
        var result = new List<(INamedTypeSymbol InterfaceType, IPropertySymbol[] Properties)>();
        if (clonableProperties.Length == 0)
            return result;

        foreach (var interfaceType in typeSymbol.AllInterfaces
            .Where(IsCompatibleCloneInterface)
            .OrderBy(i => i.ToDisplayString()))
        {
            var matchingProperties = GetInterfaceProperties(interfaceType)
                .Where(p => p.GetMethod != null)
                .Select(p => FindCompatibleProperty(clonableProperties, p))
                .Where(p => p != null)
                .OrderBy(GetProtoMemberOrder)
                .ToArray();

            if (matchingProperties.Length == 0)
                continue;

            result.Add((interfaceType, matchingProperties));
        }

        return result;
    }

    static bool IsCompatibleCloneInterface(INamedTypeSymbol interfaceType)
    {
        return interfaceType.AllInterfaces.Any(IsCloneSourceInterface);
    }

    static bool IsCloneSourceInterface(INamedTypeSymbol interfaceType) =>
        interfaceType.Name == "ICloneSource" &&
        interfaceType.ContainingNamespace?.ToDisplayString() == "Generated";

    static IEnumerable<IPropertySymbol> GetInterfaceProperties(INamedTypeSymbol interfaceType)
    {
        foreach (var property in interfaceType.GetMembers().OfType<IPropertySymbol>())
            yield return property;

        foreach (var inheritedInterface in interfaceType.AllInterfaces)
        {
            foreach (var property in inheritedInterface.GetMembers().OfType<IPropertySymbol>())
                yield return property;
        }
    }

    static IPropertySymbol FindCompatibleProperty(IPropertySymbol[] clonableProperties, IPropertySymbol interfaceProperty)
    {
        foreach (var property in clonableProperties)
        {
            if (property.Name != interfaceProperty.Name)
                continue;

            if (!SymbolEqualityComparer.Default.Equals(property.Type, interfaceProperty.Type))
                continue;

            return property;
        }

        return null;
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
                AppendTypeParameters(builder, type);
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

    static void AppendTypeParameters(StringBuilder builder, INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeParameters.Length == 0)
            return;

        builder.Append("<");
        for (var i = 0; i < typeSymbol.TypeParameters.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");

            builder.Append(typeSymbol.TypeParameters[i].Name);
        }

        builder.Append(">");
    }

    static bool IsProtoMemberProperty(IPropertySymbol property)
    {
        if (property.IsStatic || property.IsIndexer)
            return false;

        if (ReferenceEquals(property.SetMethod, null) || ReferenceEquals(property.GetMethod, null))
            return false;

        foreach (var attribute in property.GetAttributes())
        {
            var attributeName = attribute.AttributeClass == null ? string.Empty : attribute.AttributeClass.Name;
            if (attributeName == "ProtoMemberAttribute" || attributeName == "ProtoMember")
                return true;
        }

        return false;
    }

    static int GetProtoMemberOrder(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            var attributeName = attribute.AttributeClass == null ? string.Empty : attribute.AttributeClass.Name;
            if (attributeName != "ProtoMemberAttribute" && attributeName != "ProtoMember")
                continue;

            if (attribute.ConstructorArguments.Length == 0)
                return int.MaxValue;

            var value = attribute.ConstructorArguments[0].Value;
            if (value is int)
                return (int)value;

            return int.MaxValue;
        }

        return int.MaxValue;
    }

    static string GetHintName(INamedTypeSymbol typeSymbol)
    {
        var fullName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty)
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(".", "_");

        return fullName + ".CopyFrom.g.cs";
    }

    static string GetSafeName(INamedTypeSymbol typeSymbol) =>
        typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty)
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(".", "_");

    const string CLONABLE_CONTRACT_INTERFACE_SOURCE = @"// <auto-generated/>
namespace Generated
{
    public interface IClonableContract
    {
        void Clone(object other);
    }
}
";

    const string CLONE_SOURCE_INTERFACE_SOURCE = @"// <auto-generated/>
namespace Generated
{
    public interface ICloneSource
    {
    }
}
";

}
