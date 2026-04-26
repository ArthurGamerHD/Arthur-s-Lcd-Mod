using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LcdModCodeGenerator;

[Generator]
public sealed class Modules : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(postInitializationContext =>
            postInitializationContext.AddSource("IModule.g.cs", ModuleInterfacesSource));

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
                var symbols = input.Right;
                var modules = GetModules(symbols);
                var managers = GetManagers(symbols);
                foreach (var manager in managers)
                {
                    if (!IsPartial(manager) || HasRegisterModulesMethod(manager))
                        continue;

                    var source = BuildManagerSource(manager, modules);
                    sourceProductionContext.AddSource(GetHintName(manager), source);
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

    static IReadOnlyList<ModuleDefinition> GetModules(ImmutableArray<INamedTypeSymbol> symbols)
    {
        var list = new List<ModuleDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in symbols.OfType<INamedTypeSymbol>())
        {
            if (symbol.TypeKind != TypeKind.Class || symbol.IsAbstract)
                continue;

            foreach (var implementedInterface in symbol.AllInterfaces)
            {
                if (!IsGeneratedGenericInterface(implementedInterface, "IModule"))
                    continue;

                if (implementedInterface.TypeArguments.Length != 1)
                    continue;

                var interfaceType = implementedInterface.TypeArguments[0];
                if (ReferenceEquals(interfaceType, null))
                    continue;

                var key = ToDisplayName(symbol) + "|" + ToDisplayName(interfaceType);
                if (!seen.Add(key))
                    continue;

                list.Add(new ModuleDefinition(symbol, interfaceType));
            }
        }

        list.Sort((a, b) => string.CompareOrdinal(ToDisplayName(a.ModuleType), ToDisplayName(b.ModuleType)));
        return list;
    }

    static IReadOnlyList<INamedTypeSymbol> GetManagers(ImmutableArray<INamedTypeSymbol> symbols)
    {
        var list = new List<INamedTypeSymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in symbols.OfType<INamedTypeSymbol>())
        {
            if (symbol.TypeKind != TypeKind.Class)
                continue;

            if (!ImplementsGeneratedInterface(symbol, "IModuleManager"))
                continue;

            var key = ToDisplayName(symbol);
            if (seen.Add(key))
                list.Add(symbol);
        }

        return list;
    }

    static bool IsPartial(INamedTypeSymbol symbol)
    {
        foreach (var syntaxReference in symbol.DeclaringSyntaxReferences)
        {
            var declaration = syntaxReference.GetSyntax() as ClassDeclarationSyntax;
            if (ReferenceEquals(declaration, null))
                continue;

            if (declaration.Modifiers.Any(m => m.ValueText == "partial"))
                return true;
        }

        return false;
    }

    static bool HasRegisterModulesMethod(INamedTypeSymbol symbol)
    {
        return symbol.GetMembers("RegisterModules")
            .OfType<IMethodSymbol>()
            .Any(m => m.Parameters.Length == 0 && !m.IsStatic);
    }

    static bool ImplementsGeneratedInterface(INamedTypeSymbol symbol, string interfaceName)
    {
        foreach (var implementedInterface in symbol.AllInterfaces)
        {
            if (implementedInterface.Name != interfaceName)
                continue;

            if (implementedInterface.ContainingNamespace?.ToDisplayString() != "Generated")
                continue;

            return true;
        }

        return false;
    }

    static bool IsGeneratedGenericInterface(INamedTypeSymbol interfaceSymbol, string expectedName)
    {
        return interfaceSymbol.Name == expectedName &&
               interfaceSymbol.ContainingNamespace?.ToDisplayString() == "Generated";
    }

    static string BuildManagerSource(INamedTypeSymbol managerType, IReadOnlyList<ModuleDefinition> modules)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");

        var namespaceName = managerType.ContainingNamespace?.ToDisplayString();
        if (!string.IsNullOrEmpty(namespaceName) && namespaceName != "<global namespace>")
        {
            builder.Append("namespace ");
            builder.Append(namespaceName);
            builder.AppendLine();
            builder.AppendLine("{");
        }

        AppendContainingTypeDeclarations(builder, managerType, true);
        builder.AppendLine("        sealed class GeneratedSurfaceModule");
        builder.AppendLine("        {");
        builder.AppendLine("            public readonly global::System.Type InterfaceType;");
        builder.AppendLine("            public readonly global::System.Action<object> Hook;");
        builder.AppendLine("            public readonly global::System.Action<object> Unhook;");
        builder.AppendLine();
        builder.AppendLine("            public GeneratedSurfaceModule(");
        builder.AppendLine("                global::System.Type interfaceType,");
        builder.AppendLine("                global::System.Action<object> hook,");
        builder.AppendLine("                global::System.Action<object> unhook)");
        builder.AppendLine("            {");
        builder.AppendLine("                InterfaceType = interfaceType;");
        builder.AppendLine("                Hook = hook;");
        builder.AppendLine("                Unhook = unhook;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        static readonly global::System.Collections.Generic.List<GeneratedSurfaceModule> GeneratedSurfaceModules =");
        builder.AppendLine("            new global::System.Collections.Generic.List<GeneratedSurfaceModule>();");
        builder.AppendLine("        static readonly global::System.Collections.Generic.List<global::Generated.IModule> GeneratedRegisteredModules =");
        builder.AppendLine("            new global::System.Collections.Generic.List<global::Generated.IModule>();");
        builder.AppendLine("        public global::System.Collections.Generic.IReadOnlyList<global::Generated.IModule> RegisteredModules");
        builder.AppendLine("        {");
        builder.AppendLine("            get { return GeneratedRegisteredModules; }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        sealed class GeneratedModuleDebugCounter");
        builder.AppendLine("        {");
        builder.AppendLine("            public readonly string Name;");
        builder.AppendLine("            readonly global::System.Func<int> _getCount;");
        builder.AppendLine("            readonly global::System.Func<int> _getActive;");
        builder.AppendLine();
        builder.AppendLine("            public GeneratedModuleDebugCounter(string name, global::System.Func<int> getCount, global::System.Func<int> getActive)");
        builder.AppendLine("            {");
        builder.AppendLine("                Name = name;");
        builder.AppendLine("                _getCount = getCount;");
        builder.AppendLine("                _getActive = getActive;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            public int Count");
        builder.AppendLine("            {");
        builder.AppendLine("                get { return _getCount == null ? 0 : _getCount(); }");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            public int Active");
        builder.AppendLine("            {");
        builder.AppendLine("                get { return _getActive == null ? 0 : _getActive(); }");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        static readonly global::System.Collections.Generic.List<GeneratedModuleDebugCounter> GeneratedModuleDebugCounters =");
        builder.AppendLine("            new global::System.Collections.Generic.List<GeneratedModuleDebugCounter>();");
        builder.AppendLine("        bool _generatedModulesRegistered;");
        builder.AppendLine();
        builder.AppendLine("        public void RegisterModule<TInterface>(");
        builder.AppendLine("            global::System.Action<TInterface> hook,");
        builder.AppendLine("            global::System.Action<TInterface> unhook = null) where TInterface : class");
        builder.AppendLine("        {");
        builder.AppendLine("            if (hook == null)");
        builder.AppendLine("                return;");
        builder.AppendLine();
        builder.AppendLine("            TryRegisterSurfaceModule(hook, unhook);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static bool TryRegisterSurfaceModule(");
        builder.AppendLine("            global::System.Type interfaceType,");
        builder.AppendLine("            global::System.Action<object> hook,");
        builder.AppendLine("            global::System.Action<object> unhook = null)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (interfaceType == null || hook == null)");
        builder.AppendLine("                return false;");
        builder.AppendLine();
        builder.AppendLine("            var module = new GeneratedSurfaceModule(interfaceType, hook, unhook);");
        builder.AppendLine("            GeneratedSurfaceModules.Add(module);");
        builder.AppendLine();
        builder.AppendLine("            foreach (var surface in global::Graph.Apps.Abstract.SurfaceScriptBase.Instances)");
        builder.AppendLine("                TryHookSurfaceModule(surface, module);");
        builder.AppendLine();
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static bool TryRegisterSurfaceModule<TInterface>(");
        builder.AppendLine("            global::System.Action<TInterface> hook,");
        builder.AppendLine("            global::System.Action<TInterface> unhook = null) where TInterface : class");
        builder.AppendLine("        {");
        builder.AppendLine("            if (hook == null)");
        builder.AppendLine("                return false;");
        builder.AppendLine();
        builder.AppendLine("            global::System.Action<object> boxedHook = instance => hook(instance as TInterface);");
        builder.AppendLine("            global::System.Action<object> boxedUnhook = null;");
        builder.AppendLine("            if (unhook != null)");
        builder.AppendLine("                boxedUnhook = instance => unhook(instance as TInterface);");
        builder.AppendLine();
        builder.AppendLine("            return TryRegisterSurfaceModule(typeof(TInterface), boxedHook, boxedUnhook);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        internal static void HookSurfaceModules(global::Graph.Apps.Abstract.SurfaceScriptBase surface)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (surface == null)");
        builder.AppendLine("                return;");
        builder.AppendLine();
        builder.AppendLine("            for (int i = 0; i < GeneratedSurfaceModules.Count; i++)");
        builder.AppendLine("                TryHookSurfaceModule(surface, GeneratedSurfaceModules[i]);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        internal static void UnhookSurfaceModules(global::Graph.Apps.Abstract.SurfaceScriptBase surface)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (surface == null)");
        builder.AppendLine("                return;");
        builder.AppendLine();
        builder.AppendLine("            for (int i = 0; i < GeneratedSurfaceModules.Count; i++)");
        builder.AppendLine("                TryUnhookSurfaceModule(surface, GeneratedSurfaceModules[i]);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        static void TryHookSurfaceModule(");
        builder.AppendLine("            global::Graph.Apps.Abstract.SurfaceScriptBase surface,");
        builder.AppendLine("            GeneratedSurfaceModule module)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (surface == null || module == null || module.Hook == null)");
        builder.AppendLine("                return;");
        builder.AppendLine();
        builder.AppendLine("            if (!global::Sandbox.ModAPI.MyAPIGateway.Reflection.IsAssignableFrom(module.InterfaceType, surface.GetType()))");
        builder.AppendLine("                return;");
        builder.AppendLine();
        builder.AppendLine("            module.Hook(surface);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        static void TryUnhookSurfaceModule(");
        builder.AppendLine("            global::Graph.Apps.Abstract.SurfaceScriptBase surface,");
        builder.AppendLine("            GeneratedSurfaceModule module)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (surface == null || module == null || module.Unhook == null)");
        builder.AppendLine("                return;");
        builder.AppendLine();
        builder.AppendLine("            if (!global::Sandbox.ModAPI.MyAPIGateway.Reflection.IsAssignableFrom(module.InterfaceType, surface.GetType()))");
        builder.AppendLine("                return;");
        builder.AppendLine();
        builder.AppendLine("            module.Unhook(surface);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void RegisterModules()");
        builder.AppendLine("        {");
        builder.AppendLine("            if (_generatedModulesRegistered)");
        builder.AppendLine("                return;");
        builder.AppendLine("            _generatedModulesRegistered = true;");
        builder.AppendLine();

        for (var i = 0; i < modules.Count; i++)
        {
            var moduleTypeName = ToDisplayName(modules[i].ModuleType);
            var interfaceTypeName = ToDisplayName(modules[i].InterfaceType);
            var variable = "module" + i;
            builder.Append("            var ");
            builder.Append(variable);
            builder.Append(" = new ");
            builder.Append(moduleTypeName);
            builder.AppendLine("();");
            builder.Append("            RegisterModule<");
            builder.Append(interfaceTypeName);
            builder.Append(">(");
            builder.Append(variable);
            builder.AppendLine(".Hook, " + variable + ".Unhook);");
            builder.Append("            GeneratedRegisteredModules.Add(");
            builder.Append(variable);
            builder.AppendLine(");");
            builder.Append("            GeneratedModuleDebugCounters.Add(new GeneratedModuleDebugCounter(");
            builder.Append(EscapeLiteral(ShortTypeName(moduleTypeName)));
            builder.Append(", () => ");
            builder.Append(variable);
            builder.Append(".Count");
            builder.Append(", () => ");
            builder.Append(variable);
            builder.AppendLine(".ActiveCount));");
        }

        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void UpdateModules()");
        builder.AppendLine("        {");
        builder.AppendLine("            for (int i = 0; i < GeneratedRegisteredModules.Count; i++)");
        builder.AppendLine("                GeneratedRegisteredModules[i].Update();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void PostUpdateModules()");
        builder.AppendLine("        {");
        builder.AppendLine("            for (int i = 0; i < GeneratedRegisteredModules.Count; i++)");
        builder.AppendLine("                GeneratedRegisteredModules[i].PostUpdate();");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static global::System.Collections.Generic.List<string> GetModuleDebugLines()");
        builder.AppendLine("        {");
        builder.AppendLine("            var lines = new global::System.Collections.Generic.List<string>(GeneratedModuleDebugCounters.Count);");
        builder.AppendLine("            for (int i = 0; i < GeneratedModuleDebugCounters.Count; i++)");
        builder.AppendLine("            {");
        builder.AppendLine("                var entry = GeneratedModuleDebugCounters[i];");
        builder.AppendLine("                lines.Add(entry.Name + \":\" + entry.Count + \":\" + entry.Active);");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return lines;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void ClearModules()");
        builder.AppendLine("        {");
        builder.AppendLine("            if (GeneratedSurfaceModules.Count > 0)");
        builder.AppendLine("            {");
        builder.AppendLine("                foreach (var surface in global::Graph.Apps.Abstract.SurfaceScriptBase.Instances)");
        builder.AppendLine("                    UnhookSurfaceModules(surface);");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            GeneratedSurfaceModules.Clear();");
        builder.AppendLine("            GeneratedRegisteredModules.Clear();");
        builder.AppendLine("            GeneratedModuleDebugCounters.Clear();");
        builder.AppendLine("            _generatedModulesRegistered = false;");
        builder.AppendLine("        }");
        AppendContainingTypeDeclarations(builder, managerType, false);

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

    static string GetHintName(INamedTypeSymbol typeSymbol)
    {
        var fullName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty)
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(".", "_");

        return fullName + ".RegisterModules.g.cs";
    }

    static string ToDisplayName(ISymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty);

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

    static string ShortTypeName(string fullName)
    {
        if (string.IsNullOrEmpty(fullName))
            return string.Empty;

        var i = fullName.LastIndexOf('.');
        return i >= 0 && i + 1 < fullName.Length ? fullName.Substring(i + 1) : fullName;
    }

    readonly struct ModuleDefinition
    {
        public readonly INamedTypeSymbol ModuleType;
        public readonly ITypeSymbol InterfaceType;

        public ModuleDefinition(INamedTypeSymbol moduleType, ITypeSymbol interfaceType)
        {
            ModuleType = moduleType;
            InterfaceType = interfaceType;
        }
    }

    const string ModuleInterfacesSource = @"// <auto-generated/>
namespace Generated
{
    public interface IModule
    {
        int Count { get; }
        int ActiveCount { get; }
        void Update();
        void PostUpdate();
    }

    public interface IModule<TInterface> : IModule where TInterface : class
    {
        void Hook(TInterface instance);
        void Unhook(TInterface instance);
    }

    public interface IModuleManager
    {
        global::System.Collections.Generic.IReadOnlyList<IModule> RegisteredModules { get; }
        void RegisterModule<TInterface>(
            global::System.Action<TInterface> hook,
            global::System.Action<TInterface> unhook = null) where TInterface : class;
        void RegisterModules();
        void ClearModules();
        void UpdateModules();
        void PostUpdateModules();
    }
}
";
}
