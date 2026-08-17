using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace LcdMod.CodeGenerator;

[Generator]
public sealed class TypedBlockCollectionGenerator : IIncrementalGenerator
{
    const string TARGET_TYPE = "LcdMod.Client.GridData.GridLogic";
    const string TERMINAL_BLOCK_TYPE = "Sandbox.ModAPI.IMyTerminalBlock";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(context.CompilationProvider, static (sourceContext, compilation) =>
        {
            var target = compilation.GetTypeByMetadataName(TARGET_TYPE);
            var terminalBlock = compilation.GetTypeByMetadataName(TERMINAL_BLOCK_TYPE);
            if (target == null || terminalBlock == null)
                return;

            var blockTypes = GetTerminalBlockTypes(compilation, terminalBlock);
            if (blockTypes.Count == 0)
                return;

            sourceContext.AddSource("TypedBlockCollection.g.cs", BuildSource(target, blockTypes));
        });
    }

    static IReadOnlyList<BlockType> GetTerminalBlockTypes(
        Compilation compilation,
        INamedTypeSymbol terminalBlock)
    {
        var symbols = new List<INamedTypeSymbol>();
        CollectTypes(compilation.Assembly.GlobalNamespace, terminalBlock, symbols);

        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
            CollectTypes(assembly.GlobalNamespace, terminalBlock, symbols);

        var result = new List<BlockType>();
        var typeNames = new HashSet<string>(StringComparer.Ordinal);
        var memberNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var symbol in symbols.OrderBy(
                     symbol => symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                     StringComparer.Ordinal))
        {
            var typeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!typeNames.Add(typeName))
                continue;

            var memberName = GetCollectionName(symbol.Name);
            if (!memberNames.Add(memberName))
                memberName = GetQualifiedCollectionName(symbol);

            result.Add(new BlockType(symbol, typeName, memberName));
        }

        AssignParents(result);
        return result;
    }

    static void AssignParents(List<BlockType> blockTypes)
    {
        for (var i = 0; i < blockTypes.Count; i++)
        {
            var child = blockTypes[i];
            var bestParent = -1;
            var bestDepth = -1;
            for (var j = 0; j < blockTypes.Count; j++)
            {
                if (i == j)
                    continue;

                var candidate = blockTypes[j];
                if (!child.Symbol.AllInterfaces.Any(interfaceType =>
                        SymbolEqualityComparer.Default.Equals(interfaceType, candidate.Symbol)))
                    continue;

                var depth = candidate.Symbol.AllInterfaces.Length;
                if (depth > bestDepth)
                {
                    bestDepth = depth;
                    bestParent = j;
                }
            }

            child.ParentIndex = bestParent;
            blockTypes[i] = child;
        }
    }

    static void CollectTypes(
        INamespaceSymbol namespaceSymbol,
        INamedTypeSymbol terminalBlock,
        List<INamedTypeSymbol> result)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            if (IsTerminalBlockInterface(type, terminalBlock))
                result.Add(type);
        }

        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
            CollectTypes(childNamespace, terminalBlock, result);
    }

    static bool IsTerminalBlockInterface(INamedTypeSymbol type, INamedTypeSymbol terminalBlock)
    {
        if (type.TypeKind != TypeKind.Interface ||
            type.DeclaredAccessibility != Accessibility.Public ||
            type.Arity != 0 ||
            !type.Name.StartsWith("IMy", StringComparison.Ordinal))
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(type, terminalBlock))
            return true;

        return type.AllInterfaces.Any(interfaceType =>
            SymbolEqualityComparer.Default.Equals(interfaceType, terminalBlock));
    }

    static string GetCollectionName(string typeName)
    {
        var name = typeName.StartsWith("IMy", StringComparison.Ordinal)
            ? typeName.Substring(3)
            : typeName;

        if (name.EndsWith("y", StringComparison.Ordinal) &&
            name.Length > 1 &&
            !IsVowel(name[name.Length - 2]))
        {
            return name.Substring(0, name.Length - 1) + "ies";
        }

        if (name.EndsWith("s", StringComparison.Ordinal) ||
            name.EndsWith("x", StringComparison.Ordinal) ||
            name.EndsWith("z", StringComparison.Ordinal) ||
            name.EndsWith("ch", StringComparison.Ordinal) ||
            name.EndsWith("sh", StringComparison.Ordinal))
        {
            return name + "es";
        }

        return name + "s";
    }

    static bool IsVowel(char value)
    {
        switch (char.ToLowerInvariant(value))
        {
            case 'a':
            case 'e':
            case 'i':
            case 'o':
            case 'u':
                return true;
            default:
                return false;
        }
    }

    static string GetQualifiedCollectionName(INamedTypeSymbol type)
    {
        var namespaceName = type.ContainingNamespace.ToDisplayString().Replace(".", string.Empty);
        return namespaceName + GetCollectionName(type.Name);
    }

    static string BuildSource(INamedTypeSymbol target, IReadOnlyList<BlockType> blockTypes)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.Append("namespace ");
        builder.AppendLine(target.ContainingNamespace.ToDisplayString());
        builder.AppendLine("{");
        builder.AppendLine("    public sealed class TypedBlockCollection");
        builder.AppendLine("    {");
        builder.AppendLine("        public readonly global::LcdMod.Common.Mvvm.ObservableList<global::VRage.Game.ModAPI.IMyCubeBlock> All = new global::LcdMod.Common.Mvvm.ObservableList<global::VRage.Game.ModAPI.IMyCubeBlock>();");

        foreach (var blockType in blockTypes)
        {
            builder.Append("        public readonly global::LcdMod.Common.Mvvm.ObservableList<");
            builder.Append(blockType.TypeName);
            builder.Append("> ");
            builder.Append(blockType.CollectionName);
            builder.Append(" = new global::LcdMod.Common.Mvvm.ObservableList<");
            builder.Append(blockType.TypeName);
            builder.AppendLine(">();");
        }

        builder.AppendLine();
        builder.AppendLine("        public int Count => All.Count;");

        AppendAddMethod(builder, blockTypes);
        AppendRemoveMethod(builder, blockTypes);
        AppendClearMethod(builder, blockTypes);

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    static void AppendAddMethod(StringBuilder builder, IReadOnlyList<BlockType> blockTypes)
    {
        builder.AppendLine();
        builder.AppendLine("        public void Add(global::VRage.Game.ModAPI.IMyCubeBlock block)");
        builder.AppendLine("        {");
        builder.AppendLine("            All.Add(block);");

        AppendTypedDispatch(builder, "Add", blockTypes);

        builder.AppendLine("        }");
    }

    static void AppendRemoveMethod(StringBuilder builder, IReadOnlyList<BlockType> blockTypes)
    {
        builder.AppendLine();
        builder.AppendLine("        public bool Remove(global::VRage.Game.ModAPI.IMyCubeBlock block)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (!All.Remove(block))");
        builder.AppendLine("                return false;");
        builder.AppendLine();

        AppendTypedDispatch(builder, "Remove", blockTypes);

        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
    }

    static void AppendClearMethod(StringBuilder builder, IReadOnlyList<BlockType> blockTypes)
    {
        builder.AppendLine();
        builder.AppendLine("        public void Clear()");
        builder.AppendLine("        {");
        builder.AppendLine("            All.Clear();");

        foreach (var blockType in blockTypes)
        {
            builder.Append("            ");
            builder.Append(blockType.CollectionName);
            builder.AppendLine(".Clear();");
        }

        builder.AppendLine("        }");
    }

    static void AppendTypedDispatch(
        StringBuilder builder,
        string collectionMethod,
        IReadOnlyList<BlockType> blockTypes)
    {
        for (var i = 0; i < blockTypes.Count; i++)
        {
            if (blockTypes[i].ParentIndex < 0)
                AppendTypedDispatchNode(builder, collectionMethod, blockTypes, i, "block", 3);
        }
    }

    static void AppendTypedDispatchNode(
        StringBuilder builder,
        string collectionMethod,
        IReadOnlyList<BlockType> blockTypes,
        int index,
        string sourceExpression,
        int indentLevel)
    {
        var blockType = blockTypes[index];
        var indent = new string(' ', indentLevel * 4);
        builder.Append(indent);
        builder.Append("var typedBlock");
        builder.Append(index);
        builder.Append(" = ");
        builder.Append(sourceExpression);
        builder.Append(" as ");
        builder.Append(blockType.TypeName);
        builder.AppendLine(";");
        builder.Append(indent);
        builder.Append("if (typedBlock");
        builder.Append(index);
        builder.AppendLine(" != null)");
        builder.Append(indent);
        builder.AppendLine("{");
        builder.Append(indent);
        builder.Append("    ");
        builder.Append(blockType.CollectionName);
        builder.Append('.');
        builder.Append(collectionMethod);
        builder.Append("(typedBlock");
        builder.Append(index);
        builder.AppendLine(");");

        for (var childIndex = 0; childIndex < blockTypes.Count; childIndex++)
        {
            if (blockTypes[childIndex].ParentIndex == index)
                AppendTypedDispatchNode(
                    builder,
                    collectionMethod,
                    blockTypes,
                    childIndex,
                    "typedBlock" + index,
                    indentLevel + 1);
        }

        builder.Append(indent);
        builder.AppendLine("}");
    }

    struct BlockType
    {
        public readonly INamedTypeSymbol Symbol;
        public readonly string TypeName;
        public readonly string CollectionName;
        public int ParentIndex;

        public BlockType(INamedTypeSymbol symbol, string typeName, string collectionName)
        {
            Symbol = symbol;
            TypeName = typeName;
            CollectionName = collectionName;
            ParentIndex = -1;
        }
    }
}
