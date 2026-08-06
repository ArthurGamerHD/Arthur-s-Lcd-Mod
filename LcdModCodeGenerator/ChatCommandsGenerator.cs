using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LcdModCodeGenerator;

[Generator]
public sealed class ChatCommandsGenerator : IIncrementalGenerator
{
    const string ATTRIBUTE_TYPE_NAME = "Generated.ChatCommandAttribute";
    const string DEFAULT_GROUP = "lcdMod";

    static readonly DiagnosticDescriptor InvalidTarget = Error(
        "LCDCMD001",
        "Invalid chat command target",
        "Chat command '{0}' must target a static void method with no parameters, one string[] parameter, or supported typed parameters");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(postInitializationContext =>
            postInitializationContext.AddSource("ChatCommandAttribute.g.cs", ATTRIBUTE_SOURCE));

        var commandSets = context.SyntaxProvider.ForAttributeWithMetadataName(
                ATTRIBUTE_TYPE_NAME,
                static (node, _) => node is MethodDeclarationSyntax,
                static (ctx, _) => ReadCommands(ctx))
            .Where(static value => value != null)
            .Collect();

        var input = commandSets
            .Combine(context.CompilationProvider)
            .Combine(context.AnalyzerConfigOptionsProvider);
        context.RegisterSourceOutput(input, static (spc, value) =>
            Generate(spc, value.Left.Right, value.Left.Left, value.Right));
    }

    static DiagnosticDescriptor Error(string id, string title, string message) =>
        new(id, title, message, "LcdModCodeGenerator", DiagnosticSeverity.Error, true);

    static CommandSet ReadCommands(GeneratorAttributeSyntaxContext context)
    {
        var method = context.TargetSymbol as IMethodSymbol;
        if (method == null)
            return null;

        var commands = ImmutableArray.CreateBuilder<CommandInput>();
        foreach (var attribute in context.Attributes)
        {
            if (attribute.AttributeClass == null ||
                attribute.AttributeClass.ToDisplayString() != ATTRIBUTE_TYPE_NAME)
            {
                continue;
            }

            commands.Add(ReadCommand(method, attribute));
        }

        return new CommandSet(method, commands.ToImmutable());
    }

    static CommandInput ReadCommand(IMethodSymbol method, AttributeData attribute)
    {
        string command = null;
        string group = null;
        var argsRequired = 0;

        if (attribute.ConstructorArguments.Length > 0)
            command = attribute.ConstructorArguments[0].Value as string;

        if (attribute.ConstructorArguments.Length > 1)
        {
            var value = attribute.ConstructorArguments[1];
            if (value.Kind == TypedConstantKind.Primitive && value.Value is int)
                argsRequired = (int)value.Value;
            else
                group = value.Value as string;
        }

        if (attribute.ConstructorArguments.Length > 2)
        {
            var value = attribute.ConstructorArguments[2];
            if (value.Kind == TypedConstantKind.Primitive && value.Value is int)
                argsRequired = (int)value.Value;
        }

        foreach (var pair in attribute.NamedArguments)
        {
            if (pair.Key == "Command")
                command = pair.Value.Value as string;
            else if (pair.Key == "Group")
                group = pair.Value.Value as string;
            else if (pair.Key == "ArgsRequired" &&
                     pair.Value.Kind == TypedConstantKind.Primitive &&
                     pair.Value.Value is int)
                argsRequired = (int)pair.Value.Value;
        }

        var meta = GetMethodMeta(method);
        return new CommandInput(
            method,
            command,
            group,
            meta.Summary,
            meta.LocKey,
            argsRequired,
            AttributeLocation(attribute, method));
    }

    static Location AttributeLocation(AttributeData attribute, ISymbol fallback)
    {
        return attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
               ?? fallback.Locations.FirstOrDefault()
               ?? Location.None;
    }

    static void Generate(
        SourceProductionContext spc,
        Compilation compilation,
        ImmutableArray<CommandSet> commandSets,
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        if (!ShouldEmitCommandManager(compilation))
        {
            return;
        }

        var commandManagerNamespace = GetCommandManagerNamespace(optionsProvider, compilation);
        if (string.IsNullOrWhiteSpace(commandManagerNamespace))
            return;

        spc.AddSource("CommandManager.g.cs", CommandManagerSource.BuildSource(commandManagerNamespace));

        var commands = new List<CommandInput>();
        foreach (var set in commandSets)
        {
            if (set == null)
                continue;

            foreach (var command in set.Commands)
            {
                if (!IsValidCommandTarget(command.Method) || string.IsNullOrWhiteSpace(command.Command))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        InvalidTarget,
                        command.Location,
                        string.IsNullOrWhiteSpace(command.Command)
                            ? "<missing>"
                            : command.Command));
                    continue;
                }

                commands.Add(command);
            }
        }

        commands.Sort(CompareCommands);
        spc.AddSource("CommandManager.BuildChatCommands.g.cs", BuildSource(commands, commandManagerNamespace));
    }

    static bool ShouldEmitCommandManager(Compilation compilation)
    {
        return compilation.GetTypeByMetadataName("Sandbox.ModAPI.MyAPIGateway") != null &&
               compilation.GetTypeByMetadataName("VRage.Game.Components.MySessionComponentBase") != null &&
               compilation.GetTypeByMetadataName("VRage.Game.Components.MySessionComponentDescriptor") != null &&
               compilation.GetTypeByMetadataName("VRage.Game.MyObjectBuilder_SessionComponent") != null &&
               compilation.GetTypeByMetadataName("VRageMath.Color") != null;
    }

    static string GetCommandManagerNamespace(
        AnalyzerConfigOptionsProvider optionsProvider,
        Compilation compilation)
    {
        var rootNamespace = GetRootNamespace(optionsProvider, compilation);
        if (string.IsNullOrWhiteSpace(rootNamespace))
            return null;

        return rootNamespace + ".ChatCommandsGenerated";
    }

    static string GetRootNamespace(AnalyzerConfigOptionsProvider optionsProvider, Compilation compilation)
    {
        string rootNamespace;
        if (optionsProvider.GlobalOptions.TryGetValue("build_property.RootNamespace", out rootNamespace) &&
            !string.IsNullOrWhiteSpace(rootNamespace))
        {
            return SanitizeNamespace(rootNamespace.Trim());
        }

        return SanitizeNamespace(compilation.AssemblyName);
    }

    static string SanitizeNamespace(string namespaceName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
            return null;

        var parts = namespaceName.Split('.');
        var builder = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            var identifier = SanitizeIdentifier(parts[i]);
            if (string.IsNullOrWhiteSpace(identifier))
                continue;

            if (builder.Length > 0)
                builder.Append('.');

            builder.Append(identifier);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    static string SanitizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();
        var builder = new StringBuilder();
        if (!SyntaxFacts.IsIdentifierStartCharacter(value[0]))
            builder.Append('_');

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            builder.Append(SyntaxFacts.IsIdentifierPartCharacter(c) ? c : '_');
        }

        var identifier = builder.ToString();
        if (!SyntaxFacts.IsValidIdentifier(identifier))
            identifier = "_" + identifier;

        return identifier;
    }

    static int CompareCommands(CommandInput left, CommandInput right)
    {
        var compare = CompareText(left.Command, right.Command);
        if (compare != 0)
            return compare;

        compare = CompareText(left.Group ?? DEFAULT_GROUP, right.Group ?? DEFAULT_GROUP);
        if (compare != 0)
            return compare;

        compare = CompareText(GetQualifiedMethodName(left.Method), GetQualifiedMethodName(right.Method));
        if (compare != 0)
            return compare;

        var leftLocation = left.Location.GetLineSpan();
        var rightLocation = right.Location.GetLineSpan();
        compare = string.CompareOrdinal(leftLocation.Path, rightLocation.Path);
        if (compare != 0)
            return compare;

        return leftLocation.StartLinePosition.Line.CompareTo(rightLocation.StartLinePosition.Line);
    }

    static int CompareText(string left, string right)
    {
        var compare = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        if (compare != 0)
            return compare;

        return string.Compare(left, right, StringComparison.Ordinal);
    }

    static string GetQualifiedMethodName(IMethodSymbol method)
    {
        return method == null
            ? string.Empty
            : method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    static CommandMeta GetMethodMeta(IMethodSymbol method)
    {
        if (method == null)
            return CommandMeta.Empty;

        var xml = method.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml))
            return CommandMeta.Empty;

        try
        {
            var document = XDocument.Parse(xml);
            var summary = document.Descendants("summary").FirstOrDefault();
            var loc = document.Descendants("loc").FirstOrDefault();
            var locKey = NormalizeXmlAttribute(loc?.Attribute("key")?.Value);
            if (locKey == null)
                locKey = NormalizeXmlAttribute(loc?.Value);

            return new CommandMeta(NormalizeSummary(summary?.Value), locKey);
        }
        catch
        {
            return CommandMeta.Empty;
        }
    }

    static string NormalizeSummary(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value.Split(
            new[] { ' ', '\t', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 0 ? null : string.Join(" ", parts);
    }

    static string NormalizeXmlAttribute(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }

    static bool IsValidCommandTarget(IMethodSymbol method)
    {
        if (method == null || !method.IsStatic || !method.ReturnsVoid)
            return false;

        if (method.Parameters.Length == 0)
            return true;

        if (IsRawArgsCommandTarget(method))
            return true;

        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];
            if (parameter.RefKind != RefKind.None || !IsSupportedTypedParameter(parameter.Type))
                return false;
        }

        return true;
    }

    static bool IsRawArgsCommandTarget(IMethodSymbol method)
    {
        if (method == null || method.Parameters.Length != 1)
            return false;

        var parameterType = method.Parameters[0].Type as IArrayTypeSymbol;
        if (parameterType == null || parameterType.Rank != 1)
            return false;

        return parameterType.ElementType.SpecialType == SpecialType.System_String;
    }

    static bool IsTypedCommandTarget(IMethodSymbol method)
    {
        return method != null && !IsRawArgsCommandTarget(method);
    }

    static bool IsSupportedTypedParameter(ITypeSymbol type)
    {
        if (type == null)
            return false;

        ITypeSymbol nullableUnderlyingType;
        if (TryGetNullableUnderlyingType(type, out nullableUnderlyingType))
        {
            return IsSupportedTypedParameter(nullableUnderlyingType);
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
            case SpecialType.System_String:
            case SpecialType.System_Int32:
            case SpecialType.System_Int64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
                return true;
        }

        return type.ToDisplayString() == "VRageMath.Color";
    }

    static bool TryGetNullableUnderlyingType(ITypeSymbol type, out ITypeSymbol underlyingType)
    {
        underlyingType = null;
        var namedType = type as INamedTypeSymbol;
        if (namedType == null ||
            namedType.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T ||
            namedType.TypeArguments.Length != 1)
        {
            return false;
        }

        underlyingType = namedType.TypeArguments[0];
        return true;
    }

    static string BuildSource(IReadOnlyList<CommandInput> commands, string commandManagerNamespace)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.Append("namespace ");
        builder.AppendLine(commandManagerNamespace);
        builder.AppendLine("{");
        builder.AppendLine("    public sealed partial class CommandManager");
        builder.AppendLine("    {");
        builder.AppendLine("        static void BuildChatCommands()");
        builder.AppendLine("        {");
        builder.Append("            var commands = new CmdGroupInitializer(");
        builder.Append(commands.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.AppendLine(");");
        builder.Append("            commands.SetDefaultGroup(");
        builder.Append(EscapeLiteral(DEFAULT_GROUP));
        builder.AppendLine(");");

        for (var i = 0; i < commands.Count; i++)
            AppendCommand(builder, commands[i]);

        builder.AppendLine("            AddCommands(commands);");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    static void AppendCommand(StringBuilder builder, CommandInput command)
    {
        if (IsTypedCommandTarget(command.Method))
        {
            AppendTypedCommand(builder, command);
            return;
        }

        builder.Append("            commands.");
        if (string.IsNullOrWhiteSpace(command.Group))
        {
            builder.Append("Add(");
            builder.Append(EscapeLiteral(command.Command));
            builder.Append(", ");
            builder.Append(EscapeLiteral(command.Meta));
            builder.Append(", ");
            builder.Append(EscapeLiteral(command.LocKey));
            builder.Append(", ");
        }
        else
        {
            builder.Append("AddToGroup(");
            builder.Append(EscapeLiteral(command.Group));
            builder.Append(", ");
            builder.Append(EscapeLiteral(command.Command));
            builder.Append(", ");
            builder.Append(EscapeLiteral(command.Meta));
            builder.Append(", ");
            builder.Append(EscapeLiteral(command.LocKey));
            builder.Append(", ");
        }

        AppendCallback(builder, command.Method);
        if (command.ArgsRequired != 0)
        {
            builder.Append(", ");
            builder.Append(command.ArgsRequired.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        builder.AppendLine(");");
    }

    static void AppendTypedCommand(StringBuilder builder, CommandInput command)
    {
        builder.Append("            commands.");
        if (string.IsNullOrWhiteSpace(command.Group))
        {
            builder.Append("AddTyped(");
            builder.Append(EscapeLiteral(command.Command));
            builder.Append(", ");
            builder.Append(EscapeLiteral(command.Meta));
            builder.Append(", ");
            builder.Append(EscapeLiteral(command.LocKey));
            builder.Append(", ");
        }
        else
        {
            builder.Append("AddTypedToGroup(");
            builder.Append(EscapeLiteral(command.Group));
            builder.Append(", ");
            builder.Append(EscapeLiteral(command.Command));
            builder.Append(", ");
            builder.Append(EscapeLiteral(command.Meta));
            builder.Append(", ");
            builder.Append(EscapeLiteral(command.LocKey));
            builder.Append(", ");
        }

        AppendParameterMetadata(builder, command.Method);
        builder.Append(", ");
        AppendTypedCallback(builder, command.Method);
        builder.AppendLine(");");
    }

    static void AppendParameterMetadata(StringBuilder builder, IMethodSymbol method)
    {
        builder.Append("new ChatCommandParameterDefinition[] { ");
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");

            var parameter = method.Parameters[i];
            builder.Append("new ChatCommandParameterDefinition(");
            builder.Append(EscapeLiteral(parameter.Name));
            builder.Append(", typeof(");
            AppendTypeName(builder, parameter.Type);
            builder.Append(")");

            ITypeSymbol nullableUnderlyingType;
            if (TryGetNullableUnderlyingType(parameter.Type, out nullableUnderlyingType))
            {
                builder.Append(", typeof(");
                AppendTypeName(builder, nullableUnderlyingType);
                builder.Append("), true");
                if (parameter.HasExplicitDefaultValue)
                {
                    builder.Append(", ");
                    AppendDefaultValue(builder, parameter.ExplicitDefaultValue, nullableUnderlyingType);
                }
            }
            else if (parameter.HasExplicitDefaultValue)
            {
                builder.Append(", typeof(");
                AppendTypeName(builder, parameter.Type);
                builder.Append("), true, ");
                AppendDefaultValue(builder, parameter.ExplicitDefaultValue, parameter.Type);
            }

            builder.Append(")");
        }

        builder.Append(" }");
    }

    static void AppendCallback(StringBuilder builder, IMethodSymbol method)
    {
        builder.Append("delegate(string[] args) { ");
        builder.Append(method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        builder.Append(".");
        builder.Append(method.Name);
        if (method.Parameters.Length == 0)
            builder.Append("();");
        else
            builder.Append("(args);");
        builder.Append(" }");
    }

    static void AppendTypedCallback(StringBuilder builder, IMethodSymbol method)
    {
        builder.Append("new global::System.Action<object[]>(delegate(object[] values) { ");
        builder.Append(method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        builder.Append(".");
        builder.Append(method.Name);
        builder.Append("(");
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");

            builder.Append("(");
            AppendTypeName(builder, method.Parameters[i].Type);
            builder.Append(")values[");
            builder.Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.Append("]");
        }

        builder.Append("); })");
    }

    static void AppendTypeName(StringBuilder builder, ITypeSymbol type)
    {
        builder.Append(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    static void AppendDefaultValue(StringBuilder builder, object value, ITypeSymbol type)
    {
        if (value == null)
        {
            builder.Append("null");
            return;
        }

        if (type != null)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                    builder.Append((bool)value ? "true" : "false");
                    return;
                case SpecialType.System_String:
                    builder.Append(EscapeLiteral(value as string));
                    return;
                case SpecialType.System_Int32:
                    builder.Append(((int)value).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    return;
                case SpecialType.System_Int64:
                    builder.Append(((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    builder.Append("L");
                    return;
                case SpecialType.System_Single:
                    builder.Append(((float)value).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    builder.Append("f");
                    return;
                case SpecialType.System_Double:
                    builder.Append(((double)value).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    builder.Append("d");
                    return;
            }
        }

        builder.Append("null");
    }

    static string EscapeLiteral(string value)
    {
        if (value == null)
            return "null";

        return "\"" + value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n") + "\"";
    }

    sealed class CommandSet
    {
        public readonly IMethodSymbol Method;
        public readonly ImmutableArray<CommandInput> Commands;

        public CommandSet(IMethodSymbol method, ImmutableArray<CommandInput> commands)
        {
            Method = method;
            Commands = commands;
        }
    }

    sealed class CommandMeta
    {
        public static readonly CommandMeta Empty = new CommandMeta(null, null);

        public readonly string Summary;
        public readonly string LocKey;

        public CommandMeta(string summary, string locKey)
        {
            Summary = summary;
            LocKey = locKey;
        }
    }

    sealed class CommandInput
    {
        public readonly IMethodSymbol Method;
        public readonly string Command;
        public readonly string Group;
        public readonly string Meta;
        public readonly string LocKey;
        public readonly int ArgsRequired;
        public readonly Location Location;

        public CommandInput(
            IMethodSymbol method,
            string command,
            string group,
            string meta,
            string locKey,
            int argsRequired,
            Location location)
        {
            Method = method;
            Command = command;
            Group = group;
            Meta = meta;
            LocKey = locKey;
            ArgsRequired = argsRequired;
            Location = location;
        }
    }

    const string ATTRIBUTE_SOURCE = @"// <auto-generated/>
namespace Generated
{
    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    internal sealed class ChatCommandAttribute : global::System.Attribute
    {
        public ChatCommandAttribute(string command)
            : this(command, null, 0)
        {
        }

        public ChatCommandAttribute(string command, int argsRequired)
            : this(command, null, argsRequired)
        {
        }

        public ChatCommandAttribute(string command, string group)
            : this(command, group, 0)
        {
        }

        public ChatCommandAttribute(string command, string group, int argsRequired)
        {
            Command = command;
            Group = group;
            ArgsRequired = argsRequired;
        }

        public string Command { get; set; }
        public string Group { get; set; }
        public int ArgsRequired { get; set; }
    }
}";
}
