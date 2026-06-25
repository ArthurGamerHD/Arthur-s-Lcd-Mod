using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LcdModCodeGenerator;

[Generator]
public sealed class AppConfigGenerator : IIncrementalGenerator
{
    const string AppAttributeName = "LcdMod.Common.Config.Generation.LcdAppAttribute";
    const string ComponentAttributeName = "LcdMod.Common.Config.Generation.ConfigComponentAttribute";
    const string SurfaceAttributeName = "LcdMod.Common.Config.Generation.LcdSurfaceAttribute";
    const string AppBaseName = "LcdMod.Client.Apps.Abstract.App";
    const string SurfaceBaseName = "LcdMod.Client.SurfaceScripts.Abstract.SurfaceScriptBase";
    const string ConfigComponentBaseName = "LcdMod.Common.Config.Components.ConfigComponent";
    const string BlockReferenceComponentName = "LcdMod.Common.Config.Components.BlockReferenceConfigComponent";

    static readonly DiagnosticDescriptor InvalidAppId = Error("LCDCFG001", "Invalid app ID", "App '{0}' must use a stable ID greater than zero");
    static readonly DiagnosticDescriptor DuplicateAppId = Error("LCDCFG002", "Duplicate app ID", "Apps '{0}' and '{1}' both use stable ID {2}");
    static readonly DiagnosticDescriptor DuplicateAppName = Error("LCDCFG003", "Duplicate app name", "Apps '{0}' and '{1}' both generate AppType name '{2}'");
    static readonly DiagnosticDescriptor InvalidAppTarget = Error("LCDCFG004", "Invalid app declaration", "App '{0}' must be a concrete, top-level, non-generic partial class derived from App");
    static readonly DiagnosticDescriptor InvalidAppName = Error("LCDCFG005", "Invalid app name", "App '{0}' generates invalid AppType name '{1}'");
    static readonly DiagnosticDescriptor InvalidComponentTarget = Error("LCDCFG010", "Invalid component declaration", "Config component metadata on '{0}' must target a top-level, non-generic partial App-derived class");
    static readonly DiagnosticDescriptor EmptySlot = Error("LCDCFG011", "Empty component slot", "Config component on '{0}' must use a non-empty constant slot");
    static readonly DiagnosticDescriptor InvalidComponentType = Error("LCDCFG012", "Invalid component type", "Component type '{1}' on '{0}' must derive from ConfigComponent and have an accessible parameterless constructor");
    static readonly DiagnosticDescriptor DuplicateSlot = Error("LCDCFG013", "Duplicate component slot", "App '{0}' declares slot '{1}' more than once in its aggregated schema");
    static readonly DiagnosticDescriptor DuplicateProperty = Error("LCDCFG014", "Duplicate generated property", "Type '{0}' declares generated component property '{1}' more than once");
    static readonly DiagnosticDescriptor PropertyCollision = Error("LCDCFG015", "Generated property collision", "Generated component property '{1}' collides with an existing member on '{0}'");
    static readonly DiagnosticDescriptor MissingRepeatedProperty = Error("LCDCFG016", "Repeated component type needs property names", "App '{0}' uses component type '{1}' more than once; every occurrence must specify a unique PropertyName");
    static readonly DiagnosticDescriptor InvalidSemanticReference = Error("LCDCFG017", "Invalid semantic reference declaration", "Block reference component on '{0}' must use an explicit PropertyName and a semantic reference.* slot");
    static readonly DiagnosticDescriptor InvalidSurface = Error("LCDCFG020", "Invalid surface declaration", "Surface '{0}' must be a top-level, non-generic partial class derived from SurfaceScriptBase");
    static readonly DiagnosticDescriptor UnknownSurfaceApp = Error("LCDCFG021", "Unknown surface app", "Surface '{0}' references '{1}', which is not a concrete [LcdApp] class");
    static readonly DiagnosticDescriptor SurfaceAppMismatch = Error("LCDCFG022", "Surface constructs a different app", "Surface '{0}' maps to '{1}' but constructs registered app '{2}'");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var apps = context.SyntaxProvider.ForAttributeWithMetadataName(
                AppAttributeName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => ReadApp(ctx))
            .Where(static value => value != null)
            .Collect();

        var componentTargets = context.SyntaxProvider.ForAttributeWithMetadataName(
                ComponentAttributeName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => ReadComponents(ctx))
            .Where(static value => value != null)
            .Collect();

        var surfaces = context.SyntaxProvider.ForAttributeWithMetadataName(
                SurfaceAttributeName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => ReadSurface(ctx))
            .Where(static value => value != null)
            .Collect();

        var skip = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
            GetOption(options, "build_property.LcdModCodeGeneratorSkipAppConfig") == "true");

        var input = apps.Combine(componentTargets).Combine(surfaces).Combine(context.CompilationProvider).Combine(skip);
        context.RegisterSourceOutput(input, static (spc, value) =>
        {
            if (value.Right)
                return;

            var compilation = value.Left.Right;
            var surfacesInput = value.Left.Left.Right;
            var componentsInput = value.Left.Left.Left.Right;
            var appsInput = value.Left.Left.Left.Left;
            Generate(spc, compilation, appsInput, componentsInput, surfacesInput);
        });
    }

    static DiagnosticDescriptor Error(string id, string title, string message) =>
        new(id, title, message, "LcdModCodeGenerator", DiagnosticSeverity.Error, true);

    static string GetOption(AnalyzerConfigOptionsProvider options, string key)
    {
        string value;
        return options.GlobalOptions.TryGetValue(key, out value) ? value.Trim() : string.Empty;
    }

    static AppInput ReadApp(GeneratorAttributeSyntaxContext context)
    {
        var type = context.TargetSymbol as INamedTypeSymbol;
        var attribute = context.Attributes.FirstOrDefault();
        if (type == null || attribute == null)
            return null;

        var id = attribute.ConstructorArguments.Length == 0 || attribute.ConstructorArguments[0].Value == null
            ? 0
            : (int)attribute.ConstructorArguments[0].Value;
        string name = null;
        foreach (var pair in attribute.NamedArguments)
            if (pair.Key == "Name")
                name = pair.Value.Value as string;

        return new AppInput(type, id, name, AttributeLocation(attribute, type));
    }

    static ComponentTargetInput ReadComponents(GeneratorAttributeSyntaxContext context)
    {
        var type = context.TargetSymbol as INamedTypeSymbol;
        if (type == null)
            return null;

        var declarations = ImmutableArray.CreateBuilder<ComponentInput>();
        foreach (var attribute in context.Attributes)
        {
            var slot = attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string : null;
            var componentType = attribute.ConstructorArguments.Length > 1
                ? attribute.ConstructorArguments[1].Value as INamedTypeSymbol
                : null;
            string propertyName = null;
            foreach (var pair in attribute.NamedArguments)
                if (pair.Key == "PropertyName")
                    propertyName = pair.Value.Value as string;

            declarations.Add(new ComponentInput(type, slot, componentType, propertyName, AttributeLocation(attribute, type)));
        }
        return new ComponentTargetInput(type, declarations.ToImmutable());
    }

    static SurfaceInput ReadSurface(GeneratorAttributeSyntaxContext context)
    {
        var type = context.TargetSymbol as INamedTypeSymbol;
        var attribute = context.Attributes.FirstOrDefault();
        if (type == null || attribute == null)
            return null;
        var appType = attribute.ConstructorArguments.Length == 0
            ? null
            : attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
        return new SurfaceInput(type, appType, AttributeLocation(attribute, type));
    }

    static Location AttributeLocation(AttributeData attribute, INamedTypeSymbol fallback)
    {
        return attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
               ?? fallback.Locations.FirstOrDefault()
               ?? Location.None;
    }

    static void Generate(
        SourceProductionContext spc,
        Compilation compilation,
        ImmutableArray<AppInput> rawApps,
        ImmutableArray<ComponentTargetInput> rawComponentTargets,
        ImmutableArray<SurfaceInput> rawSurfaces)
    {
        var appBase = compilation.GetTypeByMetadataName(AppBaseName);
        var surfaceBase = compilation.GetTypeByMetadataName(SurfaceBaseName);
        var componentBase = compilation.GetTypeByMetadataName(ConfigComponentBaseName);
        var blockReferenceComponent = compilation.GetTypeByMetadataName(BlockReferenceComponentName);

        var componentByType = new Dictionary<INamedTypeSymbol, List<ComponentInput>>(SymbolEqualityComparer.Default);
        foreach (var target in rawComponentTargets)
        {
            if (target == null)
                continue;
            if (!IsSupportedMetadataTarget(target.Type, appBase, allowAbstract: true))
            {
                spc.ReportDiagnostic(Diagnostic.Create(InvalidComponentTarget, target.Type.Locations.FirstOrDefault() ?? Location.None, target.Type.ToDisplayString()));
                continue;
            }

            List<ComponentInput> list;
            if (!componentByType.TryGetValue(target.Type, out list))
            {
                list = new List<ComponentInput>();
                componentByType.Add(target.Type, list);
            }

            foreach (var component in target.Components)
            {
                if (string.IsNullOrWhiteSpace(component.Slot))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(EmptySlot, component.Location, target.Type.ToDisplayString()));
                    continue;
                }
                if (component.ComponentType == null
                    || !IsOrInheritsFrom(component.ComponentType, componentBase)
                    || !HasAccessibleParameterlessConstructor(component.ComponentType))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(InvalidComponentType, component.Location, target.Type.ToDisplayString(), component.ComponentType?.ToDisplayString() ?? "<missing>"));
                    continue;
                }
                if (SymbolEqualityComparer.Default.Equals(component.ComponentType, blockReferenceComponent)
                    && (string.IsNullOrWhiteSpace(component.ExplicitPropertyName)
                        || !component.Slot.StartsWith("reference.", StringComparison.Ordinal)))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(InvalidSemanticReference, component.Location, target.Type.ToDisplayString()));
                }
                component.PropertyName = string.IsNullOrWhiteSpace(component.PropertyName)
                    ? DefaultPropertyName(component.ComponentType.Name)
                    : component.PropertyName.Trim();
                list.Add(component);
            }
        }

        ValidateAndGenerateComponentProperties(spc, componentByType);

        var apps = new List<AppModel>();
        foreach (var input in rawApps)
        {
            if (input == null)
                continue;
            if (input.Id <= 0)
            {
                spc.ReportDiagnostic(Diagnostic.Create(InvalidAppId, input.Location, input.Type.ToDisplayString()));
                continue;
            }
            if (!IsSupportedMetadataTarget(input.Type, appBase, allowAbstract: false))
            {
                spc.ReportDiagnostic(Diagnostic.Create(InvalidAppTarget, input.Location, input.Type.ToDisplayString()));
                continue;
            }

            var name = string.IsNullOrWhiteSpace(input.Name) ? DefaultAppName(input.Type.Name) : input.Name.Trim();
            if (!SyntaxFacts.IsValidIdentifier(name) || SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None)
            {
                spc.ReportDiagnostic(Diagnostic.Create(InvalidAppName, input.Location, input.Type.ToDisplayString(), name));
                continue;
            }

            var schema = AggregateSchema(input.Type, componentByType, spc);
            apps.Add(new AppModel(input.Type, input.Id, name, input.Location, schema));
        }

        foreach (var group in apps.GroupBy(app => app.Id).Where(group => group.Count() > 1))
        {
            var values = group.ToArray();
            for (var i = 1; i < values.Length; i++)
                spc.ReportDiagnostic(Diagnostic.Create(DuplicateAppId, values[i].Location, values[0].Type.ToDisplayString(), values[i].Type.ToDisplayString(), group.Key));
        }
        foreach (var group in apps.GroupBy(app => app.Name, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            var values = group.ToArray();
            for (var i = 1; i < values.Length; i++)
                spc.ReportDiagnostic(Diagnostic.Create(DuplicateAppName, values[i].Location, values[0].Type.ToDisplayString(), values[i].Type.ToDisplayString(), group.Key));
        }

        apps = apps.GroupBy(app => app.Id).Select(group => group.First())
            .GroupBy(app => app.Name, StringComparer.Ordinal).Select(group => group.First())
            .OrderBy(app => app.Id).ToList();

        var appLookup = new Dictionary<INamedTypeSymbol, AppModel>(SymbolEqualityComparer.Default);
        foreach (var app in apps)
            appLookup[app.Type] = app;

        var surfaces = new List<SurfaceModel>();
        foreach (var input in rawSurfaces)
        {
            if (input == null)
                continue;
            if (!IsSupportedSurface(input.Type, surfaceBase))
            {
                spc.ReportDiagnostic(Diagnostic.Create(InvalidSurface, input.Location, input.Type.ToDisplayString()));
                continue;
            }
            AppModel app;
            if (input.AppType == null || !appLookup.TryGetValue(input.AppType, out app))
            {
                spc.ReportDiagnostic(Diagnostic.Create(UnknownSurfaceApp, input.Location, input.Type.ToDisplayString(), input.AppType?.ToDisplayString() ?? "<missing>"));
                continue;
            }
            ValidateSurfaceConstruction(spc, compilation, input, app, appLookup);
            surfaces.Add(new SurfaceModel(input.Type, app));
        }

        spc.AddSource("AppType.g.cs", BuildAppType(apps));
        spc.AddSource("AppSchemaRegistry.g.cs", BuildRegistry(apps));
        foreach (var surface in surfaces)
            spc.AddSource("Surface." + SafeHint(surface.Type) + ".AppType.g.cs", BuildSurface(surface));
    }

    static void ValidateAndGenerateComponentProperties(
        SourceProductionContext spc,
        Dictionary<INamedTypeSymbol, List<ComponentInput>> componentByType)
    {
        foreach (var pair in componentByType)
        {
            var type = pair.Key;
            var valid = new List<ComponentInput>();
            foreach (var group in pair.Value.GroupBy(component => component.PropertyName, StringComparer.Ordinal))
            {
                var entries = group.ToArray();
                if (entries.Length > 1)
                {
                    for (var i = 1; i < entries.Length; i++)
                        spc.ReportDiagnostic(Diagnostic.Create(DuplicateProperty, entries[i].Location, type.ToDisplayString(), group.Key));
                }

                var component = entries[0];
                if (!SyntaxFacts.IsValidIdentifier(component.PropertyName)
                    || SyntaxFacts.GetKeywordKind(component.PropertyName) != SyntaxKind.None
                    || HasMemberInHierarchy(type, component.PropertyName))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(PropertyCollision, component.Location, type.ToDisplayString(), component.PropertyName));
                    continue;
                }
                valid.Add(component);
            }

            if (valid.Count != 0)
                spc.AddSource("App." + SafeHint(type) + ".ConfigComponents.g.cs", BuildComponentProperties(type, valid));
        }
    }

    static List<ComponentInput> AggregateSchema(
        INamedTypeSymbol appType,
        Dictionary<INamedTypeSymbol, List<ComponentInput>> componentByType,
        SourceProductionContext spc)
    {
        var hierarchy = new Stack<INamedTypeSymbol>();
        for (var current = appType; current != null; current = current.BaseType)
            hierarchy.Push(current);

        var result = new List<ComponentInput>();
        var bySlot = new Dictionary<string, ComponentInput>(StringComparer.Ordinal);
        while (hierarchy.Count != 0)
        {
            var current = hierarchy.Pop();
            List<ComponentInput> declarations;
            if (!componentByType.TryGetValue(current, out declarations))
                continue;
            foreach (var declaration in declarations)
            {
                ComponentInput previous;
                if (bySlot.TryGetValue(declaration.Slot, out previous))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(DuplicateSlot, declaration.Location, appType.ToDisplayString(), declaration.Slot));
                    continue;
                }
                bySlot.Add(declaration.Slot, declaration);
                result.Add(declaration);
            }
        }

        foreach (var group in result.GroupBy(item => item.ComponentType, SymbolEqualityComparer.Default).Where(group => group.Count() > 1))
        {
            var entries = group.ToArray();
            if (entries.Any(entry => string.IsNullOrWhiteSpace(entry.ExplicitPropertyName)))
                spc.ReportDiagnostic(Diagnostic.Create(MissingRepeatedProperty, entries[0].Location, appType.ToDisplayString(), group.Key.ToDisplayString()));
        }

        foreach (var group in result.GroupBy(item => item.PropertyName, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            var entries = group.ToArray();
            for (var i = 1; i < entries.Length; i++)
                spc.ReportDiagnostic(Diagnostic.Create(DuplicateProperty, entries[i].Location, appType.ToDisplayString(), group.Key));
        }
        return result;
    }

    static void ValidateSurfaceConstruction(
        SourceProductionContext spc,
        Compilation compilation,
        SurfaceInput surface,
        AppModel expected,
        Dictionary<INamedTypeSymbol, AppModel> appLookup)
    {
        var constructed = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var syntaxReference in surface.Type.DeclaringSyntaxReferences)
        {
            var syntax = syntaxReference.GetSyntax() as ClassDeclarationSyntax;
            if (syntax == null)
                continue;
            var model = compilation.GetSemanticModel(syntax.SyntaxTree);
            foreach (var creation in syntax.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                var symbol = model.GetSymbolInfo(creation.Type).Symbol as INamedTypeSymbol;
                if (symbol != null && appLookup.ContainsKey(symbol))
                    constructed.Add(symbol);
            }
        }

        foreach (var actual in constructed)
        {
            if (!SymbolEqualityComparer.Default.Equals(actual, expected.Type))
                spc.ReportDiagnostic(Diagnostic.Create(SurfaceAppMismatch, surface.Location, surface.Type.ToDisplayString(), expected.Type.ToDisplayString(), actual.ToDisplayString()));
        }
    }

    static bool IsSupportedMetadataTarget(INamedTypeSymbol type, INamedTypeSymbol appBase, bool allowAbstract)
    {
        return type != null
               && type.TypeKind == TypeKind.Class
               && (allowAbstract || !type.IsAbstract)
               && type.ContainingType == null
               && type.TypeParameters.Length == 0
               && IsPartial(type)
               && IsOrInheritsFrom(type, appBase);
    }

    static bool IsSupportedSurface(INamedTypeSymbol type, INamedTypeSymbol surfaceBase)
    {
        return type != null
               && type.TypeKind == TypeKind.Class
               && !type.IsAbstract
               && type.ContainingType == null
               && type.TypeParameters.Length == 0
               && IsPartial(type)
               && InheritsFrom(type, surfaceBase);
    }

    static bool IsPartial(INamedTypeSymbol type)
    {
        return type.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
            .OfType<ClassDeclarationSyntax>()
            .Any(syntax => syntax.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)));
    }

    static bool IsOrInheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        if (type == null || baseType == null)
            return false;
        for (var current = type; current != null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
        return false;
    }

    static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        return type != null && IsOrInheritsFrom(type.BaseType, baseType);
    }

    static bool HasAccessibleParameterlessConstructor(INamedTypeSymbol type)
    {
        if (type.IsAbstract)
            return false;
        return type.InstanceConstructors.Any(ctor => ctor.Parameters.Length == 0
            && ctor.DeclaredAccessibility != Accessibility.Private
            && ctor.DeclaredAccessibility != Accessibility.Protected
            && ctor.DeclaredAccessibility != Accessibility.ProtectedAndInternal);
    }

    static bool HasMemberInHierarchy(INamedTypeSymbol type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
            if (current.GetMembers(name).Length != 0)
                return true;
        return false;
    }

    static string DefaultAppName(string typeName)
    {
        return typeName.EndsWith("App", StringComparison.Ordinal) ? typeName.Substring(0, typeName.Length - 3) : typeName;
    }

    static string DefaultPropertyName(string componentTypeName)
    {
        const string suffix = "ConfigComponent";
        var name = componentTypeName.EndsWith(suffix, StringComparison.Ordinal)
            ? componentTypeName.Substring(0, componentTypeName.Length - suffix.Length)
            : componentTypeName;
        return name + "Component";
    }

    static string BuildAppType(List<AppModel> apps)
    {
        var sb = Header();
        sb.AppendLine("namespace Generated");
        sb.AppendLine("{");
        sb.AppendLine("    public enum AppType");
        sb.AppendLine("    {");
        for (var i = 0; i < apps.Count; i++)
            sb.AppendLine("        " + apps[i].Name + " = " + apps[i].Id + (i + 1 == apps.Count ? string.Empty : ","));
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildRegistry(List<AppModel> apps)
    {
        var sb = Header();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using LcdMod.Common.Config.Components;");
        sb.AppendLine();
        sb.AppendLine("namespace Generated");
        sb.AppendLine("{");
        sb.AppendLine("    public static class AppSchemaRegistry");
        sb.AppendLine("    {");
        sb.AppendLine("        public static readonly AppType[] AllAppTypes =");
        sb.AppendLine("        {");
        for (var i = 0; i < apps.Count; i++)
            sb.AppendLine("            AppType." + apps[i].Name + (i + 1 == apps.Count ? string.Empty : ","));
        sb.AppendLine("        };");
        sb.AppendLine();
        sb.AppendLine("        public static bool IsKnownAppType(int appTypeId)");
        sb.AppendLine("        {");
        sb.AppendLine("            AppType appType;");
        sb.AppendLine("            return TryNormalizeAppType(appTypeId, out appType);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public static bool TryNormalizeAppType(int appTypeId, out AppType appType)");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (appTypeId)");
        sb.AppendLine("            {");
        foreach (var app in apps)
            sb.AppendLine("                case " + app.Id + ": appType = AppType." + app.Name + "; return true;");
        sb.AppendLine("                default: appType = default(AppType); return false;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public static AppType NormalizeAppType(int appTypeId)");
        sb.AppendLine("        {");
        sb.AppendLine("            AppType appType;");
        sb.AppendLine("            if (!TryNormalizeAppType(appTypeId, out appType))");
        sb.AppendLine("                throw new Exception(\"Unknown app type\");");
        sb.AppendLine("            return appType;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public static string GetName(AppType appType)");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (appType)");
        sb.AppendLine("            {");
        foreach (var app in apps)
            sb.AppendLine("                case AppType." + app.Name + ": return \"" + Escape(app.Name) + "\";");
        sb.AppendLine("                default: throw new Exception(\"Unknown app type\");");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public static SurfaceConfig CreateSurface(AppType appType, int surfaceIndex)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!IsKnownAppType((int)appType))");
        sb.AppendLine("                throw new Exception(\"Unknown app type\");");
        sb.AppendLine("            var surface = new SurfaceConfig");
        sb.AppendLine("            {");
        sb.AppendLine("                SurfaceIndex = surfaceIndex,");
        sb.AppendLine("                Components = new List<ConfigComponentEntry>()");
        sb.AppendLine("            };");
        sb.AppendLine("            EnsureSchema(surface, appType);");
        sb.AppendLine("            return surface;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public static bool EnsureSchema(IAppConfig config)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (config == null) throw new ArgumentNullException(nameof(config));");
        sb.AppendLine("            AppType appType;");
        sb.AppendLine("            return TryNormalizeAppType(config.AppTypeId, out appType) && EnsureSchema(config, appType);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public static bool EnsureSchema(IAppConfig config, AppType appType)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (config == null) throw new ArgumentNullException(nameof(config));");
        sb.AppendLine("            if (!IsKnownAppType((int)appType)) return false;");
        sb.AppendLine("            EnsureComponentList(config);");
        sb.AppendLine("            RemoveRegisteredIncompatibleComponents(config, appType);");
        sb.AppendLine("            switch (appType)");
        sb.AppendLine("            {");
        foreach (var app in apps)
        {
            sb.AppendLine("                case AppType." + app.Name + ":");
            foreach (var component in app.Schema)
                sb.AppendLine("                    Ensure<" + TypeName(component.ComponentType) + ">(config, " + Literal(component.Slot) + ");");
            sb.AppendLine("                    config.AppTypeId = (int)appType;");
            sb.AppendLine("                    return true;");
        }
        sb.AppendLine("                default: return false;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public static void ChangeApp(SurfaceConfig surface, AppType targetAppType)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (surface == null) throw new ArgumentNullException(nameof(surface));");
        sb.AppendLine("            if (!IsKnownAppType((int)targetAppType))");
        sb.AppendLine("                throw new Exception(\"Unknown app type\");");
        sb.AppendLine("            var target = CreateSurface(targetAppType, surface.SurfaceIndex);");
        sb.AppendLine("            target.CopyCompatibleFrom(surface);");
        sb.AppendLine("            if (surface.Components != null)");
        sb.AppendLine("            {");
        sb.AppendLine("                foreach (var entry in surface.Components)");
        sb.AppendLine("                    if (entry != null && !IsRegisteredSlot(entry.Slot))");
        sb.AppendLine("                        target.Components.Add(entry.Clone());");
        sb.AppendLine("            }");
        sb.AppendLine("            surface.Components = target.Components;");
        sb.AppendLine("            surface.AppTypeId = target.AppTypeId;");
        sb.AppendLine("            surface.LegacyAppKind = 0;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public static bool IsRegisteredSlot(string slot)");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (slot)");
        sb.AppendLine("            {");
        foreach (var slot in apps.SelectMany(app => app.Schema).Select(component => component.Slot).Distinct(StringComparer.Ordinal).OrderBy(value => value))
            sb.AppendLine("                case " + Literal(slot) + ": return true;");
        sb.AppendLine("                default: return false;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public static bool IsAllowedComponent(AppType appType, string slot, Type componentType)");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (appType)");
        sb.AppendLine("            {");
        foreach (var app in apps)
        {
            sb.AppendLine("                case AppType." + app.Name + ":");
            if (app.Schema.Count == 0)
                sb.AppendLine("                    return false;");
            else
                sb.AppendLine("                    return " + string.Join("\n                        || ", app.Schema.Select(component => "(slot == " + Literal(component.Slot) + " && componentType == typeof(" + TypeName(component.ComponentType) + "))")) + ";");
        }
        sb.AppendLine("                default: return false;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        static void EnsureComponentList(IComponentContainer config)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (config.Components == null)");
        sb.AppendLine("                config.Components = new List<ConfigComponentEntry>();");
        sb.AppendLine("            for (var i = config.Components.Count - 1; i >= 0; i--)");
        sb.AppendLine("                if (config.Components[i] == null || string.IsNullOrEmpty(config.Components[i].Slot))");
        sb.AppendLine("                    config.Components.RemoveAt(i);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        static void RemoveRegisteredIncompatibleComponents(IComponentContainer config, AppType appType)");
        sb.AppendLine("        {");
        sb.AppendLine("            for (var i = config.Components.Count - 1; i >= 0; i--)");
        sb.AppendLine("            {");
        sb.AppendLine("                var entry = config.Components[i];");
        sb.AppendLine("                if (entry != null && IsRegisteredSlot(entry.Slot)");
        sb.AppendLine("                    && !IsAllowedComponent(appType, entry.Slot, entry.Value == null ? null : entry.Value.GetType()))");
        sb.AppendLine("                    config.Components.RemoveAt(i);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        static void Ensure<T>(IComponentContainer config, string slot) where T : ConfigComponent, new()");
        sb.AppendLine("        {");
        sb.AppendLine("            ConfigComponentEntry keeper = null;");
        sb.AppendLine("            for (var i = config.Components.Count - 1; i >= 0; i--)");
        sb.AppendLine("            {");
        sb.AppendLine("                var entry = config.Components[i];");
        sb.AppendLine("                if (entry == null || entry.Slot != slot) continue;");
        sb.AppendLine("                if (keeper == null && entry.Value is T)");
        sb.AppendLine("                    keeper = entry;");
        sb.AppendLine("                else");
        sb.AppendLine("                    config.Components.RemoveAt(i);");
        sb.AppendLine("            }");
        sb.AppendLine("            if (keeper == null)");
        sb.AppendLine("                config.Components.Add(new ConfigComponentEntry(slot, new T()));");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildComponentProperties(INamedTypeSymbol type, List<ComponentInput> components)
    {
        var sb = Header();
        sb.AppendLine("using LcdMod.Common.Config.Components;");
        sb.AppendLine();
        var hasNamespace = !type.ContainingNamespace.IsGlobalNamespace;
        if (hasNamespace)
        {
            sb.AppendLine("namespace " + type.ContainingNamespace.ToDisplayString());
            sb.AppendLine("{");
        }
        var indent = hasNamespace ? "    " : string.Empty;
        sb.AppendLine(indent + AccessibilityText(type.DeclaredAccessibility) + " partial class " + Identifier(type.Name));
        sb.AppendLine(indent + "{");
        var accessibility = type.IsAbstract || !type.IsSealed ? "protected" : "private";
        foreach (var component in components.OrderBy(item => item.PropertyName, StringComparer.Ordinal))
        {
            sb.AppendLine(indent + "    " + accessibility + " " + TypeName(component.ComponentType) + " " + Identifier(component.PropertyName) +
                          " => this.Config.GetComponent<" + TypeName(component.ComponentType) + ">(" + Literal(component.Slot) + ");");
        }
        sb.AppendLine(indent + "}");
        if (hasNamespace)
            sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildSurface(SurfaceModel surface)
    {
        var sb = Header();
        var hasNamespace = !surface.Type.ContainingNamespace.IsGlobalNamespace;
        if (hasNamespace)
        {
            sb.AppendLine("namespace " + surface.Type.ContainingNamespace.ToDisplayString());
            sb.AppendLine("{");
        }
        var indent = hasNamespace ? "    " : string.Empty;
        sb.AppendLine(indent + AccessibilityText(surface.Type.DeclaredAccessibility) + " partial class " + Identifier(surface.Type.Name));
        sb.AppendLine(indent + "{");
        sb.AppendLine(indent + "    protected override global::Generated.AppType AppType => global::Generated.AppType." + surface.App.Name + ";");
        sb.AppendLine(indent + "}");
        if (hasNamespace)
            sb.AppendLine("}");
        return sb.ToString();
    }

    static StringBuilder Header()
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        return sb;
    }

    static string TypeName(INamedTypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    static string Identifier(string value) => SyntaxFacts.GetKeywordKind(value) == SyntaxKind.None ? value : "@" + value;
    static string Literal(string value) => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value ?? string.Empty, true);
    static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    static string SafeHint(INamedTypeSymbol type) => type.ToDisplayString().Replace('<', '_').Replace('>', '_').Replace('.', '_').Replace('+', '_');

    static string AccessibilityText(Accessibility accessibility)
    {
        switch (accessibility)
        {
            case Accessibility.Public: return "public";
            case Accessibility.Internal: return "internal";
            case Accessibility.Private: return "private";
            case Accessibility.Protected: return "protected";
            case Accessibility.ProtectedAndInternal: return "private protected";
            case Accessibility.ProtectedOrInternal: return "protected internal";
            default: return "internal";
        }
    }

    sealed class AppInput
    {
        public AppInput(INamedTypeSymbol type, int id, string name, Location location) { Type = type; Id = id; Name = name; Location = location; }
        public INamedTypeSymbol Type { get; }
        public int Id { get; }
        public string Name { get; }
        public Location Location { get; }
    }

    sealed class ComponentTargetInput
    {
        public ComponentTargetInput(INamedTypeSymbol type, ImmutableArray<ComponentInput> components) { Type = type; Components = components; }
        public INamedTypeSymbol Type { get; }
        public ImmutableArray<ComponentInput> Components { get; }
    }

    sealed class ComponentInput
    {
        public ComponentInput(INamedTypeSymbol declaringType, string slot, INamedTypeSymbol componentType, string propertyName, Location location)
        {
            DeclaringType = declaringType; Slot = slot; ComponentType = componentType; ExplicitPropertyName = propertyName; PropertyName = propertyName; Location = location;
        }
        public INamedTypeSymbol DeclaringType { get; }
        public string Slot { get; }
        public INamedTypeSymbol ComponentType { get; }
        public string ExplicitPropertyName { get; }
        public string PropertyName { get; set; }
        public Location Location { get; }
    }

    sealed class SurfaceInput
    {
        public SurfaceInput(INamedTypeSymbol type, INamedTypeSymbol appType, Location location) { Type = type; AppType = appType; Location = location; }
        public INamedTypeSymbol Type { get; }
        public INamedTypeSymbol AppType { get; }
        public Location Location { get; }
    }

    sealed class AppModel
    {
        public AppModel(INamedTypeSymbol type, int id, string name, Location location, List<ComponentInput> schema) { Type = type; Id = id; Name = name; Location = location; Schema = schema; }
        public INamedTypeSymbol Type { get; }
        public int Id { get; }
        public string Name { get; }
        public Location Location { get; }
        public List<ComponentInput> Schema { get; }
    }

    sealed class SurfaceModel
    {
        public SurfaceModel(INamedTypeSymbol type, AppModel app) { Type = type; App = app; }
        public INamedTypeSymbol Type { get; }
        public AppModel App { get; }
    }
}
