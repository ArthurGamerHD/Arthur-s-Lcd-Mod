using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LcdMod.CodeGenerator;

[Generator]
public sealed class TerminalSettingControlsGenerator : IIncrementalGenerator
{
    const string TERMINAL_SLIDER_TYPE_NAME = "Generated.TerminalSlider";
    const string TERMINAL_SWITCH_TYPE_NAME = "Generated.TerminalSwitch";
    const string TERMINAL_COLOR_TYPE_NAME = "Generated.TerminalColor";
    const string CONFIG_COMPONENT_ATTRIBUTE_NAME = "LcdMod.Common.Config.Generation.ConfigComponentAttribute";
    const string CONFIG_COMPONENT_BASE_NAME = "LcdMod.Common.Config.Components.ConfigComponent";
    const string OPTIONAL_VALUE_TYPE_NAME = "LcdMod.Common.Config.OptionalValue`1";
    const string COLOR_TYPE_NAME = "VRageMath.Color";
    const string COLOR_RESOLVER_TYPE_NAME = "LcdMod.Common.Config.Components.ColorConfigComponentExtensions";
    const string TERMINAL_BLOCK_TYPE_NAME = "Sandbox.ModAPI.IMyTerminalBlock";
    const string RUNTIME_WRAPPER_NAME = "LcdMod.Client.Terminal.Controls.TerminalControlsWrapper";

    static readonly DiagnosticDescriptor InvalidTarget = Error(
        "LCDTERM101", "Invalid terminal setting target",
        "Terminal control metadata on '{0}' must target a public instance property with public get and set accessors on a ConfigComponent");
    static readonly DiagnosticDescriptor InvalidRegistrationId = Error(
        "LCDTERM102", "Invalid terminal registration ID",
        "Terminal setting '{0}' must use a RegistrationId greater than zero");
    static readonly DiagnosticDescriptor DuplicateRegistrationId = Error(
        "LCDTERM103", "Duplicate terminal registration ID",
        "Terminal settings '{0}' and '{1}' both use RegistrationId {2}");
    static readonly DiagnosticDescriptor InvalidControlId = Error(
        "LCDTERM104", "Invalid terminal control ID",
        "Terminal setting '{0}' must use a non-empty ControlId");
    static readonly DiagnosticDescriptor DuplicateControlId = Error(
        "LCDTERM105", "Duplicate terminal control ID",
        "Terminal settings '{0}' and '{1}' both use ControlId '{2}'");
    static readonly DiagnosticDescriptor InvalidSlot = Error(
        "LCDTERM106", "Invalid terminal setting slot",
        "Terminal setting '{0}' must name a slot used by component '{1}', or the component must be registered under exactly one slot");
    static readonly DiagnosticDescriptor UnsupportedSliderType = Error(
        "LCDTERM107", "Unsupported terminal slider property",
        "Slider setting '{0}' has unsupported property type '{1}'");
    static readonly DiagnosticDescriptor InvalidSliderLimits = Error(
        "LCDTERM108", "Invalid terminal slider limits",
        "Slider setting '{0}' must use finite limits where minimum is less than maximum and both values fit the property type");
    static readonly DiagnosticDescriptor InvalidSwitchType = Error(
        "LCDTERM109", "Unsupported terminal switch property",
        "Switch setting '{0}' must have type bool");
    static readonly DiagnosticDescriptor InvalidQuantum = Error(
        "LCDTERM110", "Invalid terminal slider quantum",
        "Slider setting '{0}' must use a finite non-negative Quantum");
    static readonly DiagnosticDescriptor InvalidColorType = Error(
        "LCDTERM111", "Unsupported terminal color property",
        "Color setting '{0}' must have type VRageMath.Color or OptionalValue<VRageMath.Color>");
    static readonly DiagnosticDescriptor MissingColorResolver = Error(
        "LCDTERM112", "Missing terminal color resolver",
        "Optional color setting '{0}' must have exactly one public static resolver named '{1}' accepting the component and optionally IMyTerminalBlock");
    static readonly DiagnosticDescriptor InvalidCustomColorRequirement = Error(
        "LCDTERM113", "Invalid custom-color dependency",
        "Color setting '{0}' uses RequiresCustomColor but component '{1}' has no public bool CustomizedColors property");
    // The terminal throws on a repeated RegistrationId and then shows none of the mod's controls.
    static readonly DiagnosticDescriptor HandwrittenRegistrationIdClash = new(
        "LCDTERM114", "Terminal registration ID also registered by hand",
        "Terminal setting '{0}' uses RegistrationId {1}, which a hand-written registration also uses ({2}); the terminal would fail to load its controls",
        "LcdModCodeGenerator", DiagnosticSeverity.Warning, true);
    static readonly DiagnosticDescriptor DuplicateHandwrittenRegistrationId = new(
        "LCDTERM115", "Duplicate hand-written terminal registration ID",
        "RegistrationId {0} is registered by hand more than once ({1}); the terminal would fail to load its controls",
        "LcdModCodeGenerator", DiagnosticSeverity.Warning, true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(postInitializationContext =>
            postInitializationContext.AddSource("TerminalControlAttributeConfigTypes.g.cs", ATTRIBUTE_CONFIG_TYPE_SOURCE));

        var settings = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is PropertyDeclarationSyntax property && property.AttributeLists.Count > 0,
                static (ctx, _) => ReadSetting(ctx))
            .Where(static value => value != null)
            .Collect();

        var componentSlots = context.SyntaxProvider.ForAttributeWithMetadataName(
                CONFIG_COMPONENT_ATTRIBUTE_NAME,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => ReadComponentSlots(ctx))
            .Where(static value => value != null)
            .Collect();

        // Calls passing a constant to an int parameter named registrationId: TerminalManager's AddRegistration and
        // direct TerminalControlRegistration constructions.
        var handwritten = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => (node is InvocationExpressionSyntax invocation && invocation.ArgumentList.Arguments.Count > 0) ||
                                    (node is ObjectCreationExpressionSyntax creation && creation.ArgumentList != null &&
                                     creation.ArgumentList.Arguments.Count > 0),
                static (ctx, _) => ReadHandwrittenRegistration(ctx))
            .Where(static value => value != null)
            .Collect();

        var input = settings.Combine(componentSlots).Combine(handwritten).Combine(context.CompilationProvider);
        context.RegisterSourceOutput(input, static (spc, value) =>
        {
            var compilation = value.Right;
            var registrations = value.Left.Right;
            var slots = value.Left.Left.Right;
            var settingsInput = value.Left.Left.Left;
            Generate(spc, compilation, settingsInput, slots, registrations);
        });
    }

    static DiagnosticDescriptor Error(string id, string title, string message) =>
        new(id, title, message, "LcdModCodeGenerator", DiagnosticSeverity.Error, true);

    sealed class HandwrittenRegistration
    {
        public HandwrittenRegistration(int registrationId, Location location, string display)
        {
            RegistrationId = registrationId;
            Location = location;
            Display = display;
        }

        public int RegistrationId { get; }
        public Location Location { get; }
        public string Display { get; }
    }

    static HandwrittenRegistration ReadHandwrittenRegistration(GeneratorSyntaxContext context)
    {
        ArgumentListSyntax arguments = context.Node is InvocationExpressionSyntax invocation
            ? invocation.ArgumentList
            : ((ObjectCreationExpressionSyntax)context.Node).ArgumentList;
        var method = context.SemanticModel.GetSymbolInfo(context.Node).Symbol as IMethodSymbol;
        if (method == null || arguments == null)
            return null;
        IParameterSymbol parameter = method.Parameters.FirstOrDefault(item =>
            item.Name == "registrationId" && item.Type.SpecialType == SpecialType.System_Int32);
        if (parameter == null)
            return null;

        ArgumentSyntax argument = arguments.Arguments.FirstOrDefault(item =>
            item.NameColon != null && item.NameColon.Name.Identifier.ValueText == parameter.Name);
        if (argument == null && parameter.Ordinal < arguments.Arguments.Count &&
            arguments.Arguments[parameter.Ordinal].NameColon == null)
            argument = arguments.Arguments[parameter.Ordinal];
        if (argument == null)
            return null;
        Optional<object> constant = context.SemanticModel.GetConstantValue(argument.Expression);
        if (!constant.HasValue || !(constant.Value is int))
            return null;

        var location = argument.GetLocation();
        var line = location.GetLineSpan();
        string display = System.IO.Path.GetFileName(line.Path) + ":" + (line.StartLinePosition.Line + 1);
        return new HandwrittenRegistration((int)constant.Value, location, display);
    }

    static SettingInput ReadSetting(GeneratorSyntaxContext context)
    {
        var declaration = context.Node as PropertyDeclarationSyntax;
        var property = declaration == null ? null : context.SemanticModel.GetDeclaredSymbol(declaration);
        if (property == null)
            return null;

        foreach (var attribute in property.GetAttributes())
        {
            if (!TryGetSettingKind(attribute.AttributeClass, out var kind))
                continue;

            switch (kind)
            {
                case SettingKind.Slider:
                    return new SettingInput(
                        kind,
                        property,
                        GetInt(attribute, 0),
                        GetString(attribute, 1),
                        GetString(attribute, 2),
                        GetFloat(attribute, 3),
                        GetFloat(attribute, 4),
                        GetString(attribute, 5),
                        GetNamedString(attribute, "Tooltip"),
                        GetNamedString(attribute, "Slot"),
                        GetNamedString(attribute, "WriterSuffix"),
                        GetNamedBool(attribute, "RequiresAdvancedTweakables"),
                        GetNamedFloat(attribute, "Quantum"),
                        null,
                        null,
                        null,
                        0f,
                        false,
                        false,
                        GetNamedString(attribute, "OnlyApps"),
                        AttributeLocation(attribute, property));
                case SettingKind.Switch:
                    return new SettingInput(
                        kind,
                        property,
                        GetInt(attribute, 0),
                        GetString(attribute, 1),
                        GetString(attribute, 2),
                        0f,
                        0f,
                        null,
                        GetNamedString(attribute, "Tooltip"),
                        GetNamedString(attribute, "Slot"),
                        null,
                        GetNamedBool(attribute, "RequiresAdvancedTweakables"),
                        0f,
                        GetNamedString(attribute, "OnText"),
                        GetNamedString(attribute, "OffText"),
                        GetNamedString(attribute, "TitleSuffix"),
                        GetNamedFloat(attribute, "MinimumHeightToWidthRatio"),
                        GetNamedBool(attribute, "RefreshTerminalOnSet"),
                        false,
                        GetNamedString(attribute, "OnlyApps"),
                        AttributeLocation(attribute, property));
                default:
                    return new SettingInput(
                        kind,
                        property,
                        GetInt(attribute, 0),
                        GetString(attribute, 1),
                        GetString(attribute, 2),
                        0f,
                        0f,
                        null,
                        GetNamedString(attribute, "Tooltip"),
                        GetNamedString(attribute, "Slot"),
                        null,
                        GetNamedBool(attribute, "RequiresAdvancedTweakables"),
                        0f,
                        null,
                        null,
                        null,
                        0f,
                        false,
                        GetNamedBool(attribute, "RequiresCustomColor"),
                        GetNamedString(attribute, "OnlyApps"),
                        AttributeLocation(attribute, property));
            }
        }

        return null;
    }

    static ComponentSlotTarget ReadComponentSlots(GeneratorAttributeSyntaxContext context)
    {
        var target = context.TargetSymbol as INamedTypeSymbol;
        if (target == null)
            return null;

        var values = ImmutableArray.CreateBuilder<ComponentSlotInput>();
        foreach (var attribute in context.Attributes)
        {
            var slot = GetString(attribute, 0);
            var componentType = attribute.ConstructorArguments.Length > 1
                ? attribute.ConstructorArguments[1].Value as INamedTypeSymbol
                : null;
            values.Add(new ComponentSlotInput(componentType, slot));
        }
        return new ComponentSlotTarget(values.ToImmutable());
    }

    static void Generate(
        SourceProductionContext spc,
        Compilation compilation,
        ImmutableArray<SettingInput> settings,
        ImmutableArray<ComponentSlotTarget> rawSlots,
        ImmutableArray<HandwrittenRegistration> handwritten)
    {
        // Config-only test projects deliberately do not reference the client terminal runtime.
        if (compilation.GetTypeByMetadataName(RUNTIME_WRAPPER_NAME) == null)
            return;

        var componentBase = compilation.GetTypeByMetadataName(CONFIG_COMPONENT_BASE_NAME);
        var colorType = compilation.GetTypeByMetadataName(COLOR_TYPE_NAME);
        var optionalValueType = compilation.GetTypeByMetadataName(OPTIONAL_VALUE_TYPE_NAME);
        var colorResolverType = compilation.GetTypeByMetadataName(COLOR_RESOLVER_TYPE_NAME);
        var terminalBlockType = compilation.GetTypeByMetadataName(TERMINAL_BLOCK_TYPE_NAME);
        if (componentBase == null)
            return;

        var slotsByComponent = BuildSlotMap(rawSlots);
        var rawSettings = settings.Where(item => item != null).ToArray();
        var valid = new List<SettingModel>();

        foreach (var input in rawSettings)
        {
            var property = input.Property;
            var componentType = property.ContainingType;
            var displayName = componentType.ToDisplayString() + "." + property.Name;

            if (!IsValidProperty(property, componentBase))
            {
                spc.ReportDiagnostic(Diagnostic.Create(InvalidTarget, input.Location, displayName));
                continue;
            }
            if (input.RegistrationId <= 0)
            {
                spc.ReportDiagnostic(Diagnostic.Create(InvalidRegistrationId, input.Location, displayName));
                continue;
            }
            if (string.IsNullOrWhiteSpace(input.ControlId))
            {
                spc.ReportDiagnostic(Diagnostic.Create(InvalidControlId, input.Location, displayName));
                continue;
            }

            string slot;
            if (!TryResolveSlot(componentType, input.Slot, slotsByComponent, out slot))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    InvalidSlot,
                    input.Location,
                    displayName,
                    componentType.ToDisplayString()));
                continue;
            }

            if (input.Kind == SettingKind.Slider)
            {
                if (!IsSupportedSliderType(property.Type))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        UnsupportedSliderType,
                        input.Location,
                        displayName,
                        property.Type.ToDisplayString()));
                    continue;
                }
                if (!ValidLimits(input.Minimum, input.Maximum, property.Type))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(InvalidSliderLimits, input.Location, displayName));
                    continue;
                }
                if (float.IsNaN(input.Quantum) || float.IsInfinity(input.Quantum) || input.Quantum < 0f)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(InvalidQuantum, input.Location, displayName));
                    continue;
                }
            }
            else if (input.Kind == SettingKind.Switch)
            {
                if (property.Type.SpecialType != SpecialType.System_Boolean)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(InvalidSwitchType, input.Location, displayName));
                    continue;
                }
            }
            else
            {
                bool optionalColor;
                if (colorType == null || optionalValueType == null
                    || !TryGetColorShape(property.Type, colorType, optionalValueType, out optionalColor))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(InvalidColorType, input.Location, displayName));
                    continue;
                }

                IMethodSymbol resolver = null;
                if (optionalColor)
                {
                    var resolverName = "Resolve" + property.Name;
                    var resolvers = colorResolverType == null || terminalBlockType == null
                        ? Array.Empty<IMethodSymbol>()
                        : FindColorResolvers(
                                colorResolverType,
                                resolverName,
                                componentType,
                                colorType,
                                terminalBlockType)
                            .ToArray();
                    if (resolvers.Length != 1)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            MissingColorResolver,
                            input.Location,
                            displayName,
                            resolverName));
                        continue;
                    }
                    resolver = resolvers[0];
                }

                if (input.RequiresCustomColor && !HasCustomColorFlag(componentType))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        InvalidCustomColorRequirement,
                        input.Location,
                        displayName,
                        componentType.ToDisplayString()));
                    continue;
                }

                valid.Add(new SettingModel(
                    input,
                    componentType,
                    slot,
                    GeneratedClassName(input),
                    optionalColor,
                    resolver));
                continue;
            }

            valid.Add(new SettingModel(input, componentType, slot, GeneratedClassName(input), false, null));
        }

        foreach (var group in valid.GroupBy(item => item.Input.RegistrationId).Where(group => group.Count() > 1))
        {
            var items = group.ToArray();
            for (var i = 1; i < items.Length; i++)
                spc.ReportDiagnostic(Diagnostic.Create(
                    DuplicateRegistrationId,
                    items[i].Input.Location,
                    Display(items[0]),
                    Display(items[i]),
                    group.Key));
        }
        foreach (var group in valid.GroupBy(item => item.Input.ControlId, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            var items = group.ToArray();
            for (var i = 1; i < items.Length; i++)
                spc.ReportDiagnostic(Diagnostic.Create(
                    DuplicateControlId,
                    items[i].Input.Location,
                    Display(items[0]),
                    Display(items[i]),
                    group.Key));
        }

        var handwrittenById = handwritten.Where(item => item != null).GroupBy(item => item.RegistrationId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var pair in handwrittenById.Where(pair => pair.Value.Length > 1))
        {
            string places = string.Join(", ", pair.Value.Select(item => item.Display));
            foreach (var item in pair.Value.Skip(1))
                spc.ReportDiagnostic(Diagnostic.Create(DuplicateHandwrittenRegistrationId, item.Location, pair.Key, places));
        }
        foreach (var model in valid)
        {
            HandwrittenRegistration[] clashes;
            if (handwrittenById.TryGetValue(model.Input.RegistrationId, out clashes))
                spc.ReportDiagnostic(Diagnostic.Create(
                    HandwrittenRegistrationIdClash,
                    model.Input.Location,
                    Display(model),
                    model.Input.RegistrationId,
                    string.Join(", ", clashes.Select(item => item.Display))));
        }

        var duplicateIds = new HashSet<int>(valid.GroupBy(item => item.Input.RegistrationId)
            .Where(group => group.Count() > 1).Select(group => group.Key));
        var duplicateControlIds = new HashSet<string>(valid.GroupBy(item => item.Input.ControlId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key), StringComparer.Ordinal);
        valid = valid.Where(item => !duplicateIds.Contains(item.Input.RegistrationId)
                                    && !duplicateControlIds.Contains(item.Input.ControlId)).ToList();

        foreach (var model in valid)
        {
            string source;
            switch (model.Input.Kind)
            {
                case SettingKind.Slider:
                    source = BuildSlider(model);
                    break;
                case SettingKind.Switch:
                    source = BuildSwitch(model);
                    break;
                default:
                    source = BuildColor(model);
                    break;
            }
            spc.AddSource("TerminalControl." + model.ClassName + ".g.cs", source);
        }
        spc.AddSource("GeneratedTerminalControlRegistry.g.cs", BuildRegistry(valid));
    }

    static Dictionary<INamedTypeSymbol, HashSet<string>> BuildSlotMap(ImmutableArray<ComponentSlotTarget> rawSlots)
    {
        var result = new Dictionary<INamedTypeSymbol, HashSet<string>>(SymbolEqualityComparer.Default);
        foreach (var target in rawSlots)
        {
            if (target == null)
                continue;
            foreach (var entry in target.Slots)
            {
                if (entry.ComponentType == null || string.IsNullOrWhiteSpace(entry.Slot))
                    continue;
                HashSet<string> slots;
                if (!result.TryGetValue(entry.ComponentType, out slots))
                {
                    slots = new HashSet<string>(StringComparer.Ordinal);
                    result.Add(entry.ComponentType, slots);
                }
                slots.Add(entry.Slot);
            }
        }
        return result;
    }

    static bool TryGetSettingKind(INamedTypeSymbol attributeType, out SettingKind kind)
    {
        kind = default;
        if (attributeType == null)
            return false;

        foreach (var implementedInterface in attributeType.AllInterfaces)
        {
            if (!IsAttributeConfigTypeInterface(implementedInterface))
                continue;

            var configType = implementedInterface.TypeArguments.Length == 1
                ? implementedInterface.TypeArguments[0]
                : null;
            var configTypeName = configType == null
                ? null
                : configType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            switch (configTypeName)
            {
                case "global::" + TERMINAL_SLIDER_TYPE_NAME:
                    kind = SettingKind.Slider;
                    return true;
                case "global::" + TERMINAL_SWITCH_TYPE_NAME:
                    kind = SettingKind.Switch;
                    return true;
                case "global::" + TERMINAL_COLOR_TYPE_NAME:
                    kind = SettingKind.Color;
                    return true;
            }
        }

        return false;
    }

    static bool IsAttributeConfigTypeInterface(INamedTypeSymbol type)
    {
        if (type == null)
            return false;

        var definition = type.OriginalDefinition;
        return definition.MetadataName == "IAttributeConfigType`1"
               && definition.ContainingNamespace != null
               && definition.ContainingNamespace.ToDisplayString() == "Generated";
    }

    static bool TryResolveSlot(
        INamedTypeSymbol componentType,
        string explicitSlot,
        Dictionary<INamedTypeSymbol, HashSet<string>> slotsByComponent,
        out string slot)
    {
        HashSet<string> slots;
        if (!slotsByComponent.TryGetValue(componentType, out slots))
        {
            slot = null;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(explicitSlot))
        {
            slot = explicitSlot;
            return slots.Contains(explicitSlot);
        }

        if (slots.Count == 1)
        {
            slot = slots.First();
            return true;
        }

        slot = null;
        return false;
    }

    static bool IsValidProperty(IPropertySymbol property, INamedTypeSymbol componentBase)
    {
        return property != null
               && !property.IsStatic
               && !property.IsIndexer
               && property.DeclaredAccessibility == Accessibility.Public
               && property.GetMethod != null
               && property.GetMethod.DeclaredAccessibility == Accessibility.Public
               && property.SetMethod != null
               && property.SetMethod.DeclaredAccessibility == Accessibility.Public
               && IsOrInheritsFrom(property.ContainingType, componentBase);
    }

    static bool IsOrInheritsFrom(INamedTypeSymbol type, INamedTypeSymbol expectedBase)
    {
        for (var current = type; current != null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current, expectedBase))
                return true;
        return false;
    }

    static bool IsSupportedSliderType(ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Int32:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
                return true;
            default:
                return false;
        }
    }

    static bool ValidLimits(float minimum, float maximum, ITypeSymbol type)
    {
        if (float.IsNaN(minimum) || float.IsInfinity(minimum)
            || float.IsNaN(maximum) || float.IsInfinity(maximum)
            || minimum >= maximum)
            return false;

        switch (type.SpecialType)
        {
            case SpecialType.System_SByte: return minimum >= sbyte.MinValue && maximum <= sbyte.MaxValue;
            case SpecialType.System_Byte: return minimum >= byte.MinValue && maximum <= byte.MaxValue;
            case SpecialType.System_Int16: return minimum >= short.MinValue && maximum <= short.MaxValue;
            case SpecialType.System_UInt16: return minimum >= ushort.MinValue && maximum <= ushort.MaxValue;
            default: return true;
        }
    }

    static string BuildSlider(SettingModel model)
    {
        var input = model.Input;
        TypeName(input.Property.Type);
        var componentType = TypeName(model.ComponentType);
        var sb = Header();
        sb.AppendLine("namespace LcdMod.Client.Terminal.Controls.GeneratedSettings");
        sb.AppendLine("{");
        sb.AppendLine("    internal sealed class " + model.ClassName + " : global::LcdMod.Client.Terminal.Controls.TerminalControlsWrapper");
        sb.AppendLine("    {");
        sb.AppendLine("        static readonly " + componentType + " DefaultComponent = new " + componentType + "();");
        sb.AppendLine();
        sb.AppendLine("        public override global::Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl TerminalControl { get; }");
        if (input.RequiresAdvancedTweakables)
            sb.AppendLine("        protected override bool RequiresAdvancedTweakables => true;");
        sb.AppendLine();
        sb.AppendLine("        public " + model.ClassName + "()");
        sb.AppendLine("        {");
        sb.AppendLine("            var control = CreateControl<global::Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlSlider>(" + Literal(input.ControlId) + ");");
        sb.AppendLine("            control.Getter = Getter;");
        sb.AppendLine("            control.Setter = Setter;");
        sb.AppendLine("            control.Visible = Visible;");
        sb.AppendLine("            control.SetLimits(" + FloatLiteral(input.Minimum) + ", " + FloatLiteral(input.Maximum) + ");");
        sb.AppendLine("            control.Writer = Writer;");
        sb.AppendLine("            control.Title = global::VRage.Utils.MyStringId.GetOrCompute(" + Literal(input.Title ?? string.Empty) + ");");
        if (!string.IsNullOrEmpty(input.Tooltip))
            sb.AppendLine("            control.Tooltip = global::VRage.Utils.MyStringId.GetOrCompute(" + Literal(input.Tooltip) + ");");
        sb.AppendLine("            TerminalControl = control;");
        sb.AppendLine("        }");
        sb.AppendLine();
        AppendVisibility(sb, model);
        sb.AppendLine();
        sb.AppendLine("        void Writer(global::Sandbox.ModAPI.IMyTerminalBlock block, global::System.Text.StringBuilder text)");
        sb.AppendLine("        {");
        sb.AppendLine("            text.Append(Getter(block).ToString(" + Literal(input.WriterFormat ?? string.Empty) + ")); ");
        if (!string.IsNullOrEmpty(input.WriterSuffix))
            sb.AppendLine("            text.Append(" + Literal(input.WriterSuffix) + ");");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        void Setter(global::Sandbox.ModAPI.IMyTerminalBlock block, float value)");
        sb.AppendLine("        {");
        sb.AppendLine("            value = Normalize(value);");
        sb.AppendLine("            global::LcdMod.Client.Config.ConfigManager.ModifyComponentForCurrentSurface<" + componentType + ">(");
        sb.AppendLine("                block,");
        sb.AppendLine("                " + Literal(model.Slot) + ",");
        sb.AppendLine("                component => component." + Identifier(input.Property.Name) + " = " + ConvertFromFloat("value", input.Property.Type) + ");");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        float Getter(global::Sandbox.ModAPI.IMyTerminalBlock block)");
        sb.AppendLine("        {");
        sb.AppendLine("            var component = global::LcdMod.Client.Config.ConfigManager.GetComponentForCurrentSurface<" + componentType + ">(");
        sb.AppendLine("                block,");
        sb.AppendLine("                " + Literal(model.Slot) + ");");
        sb.AppendLine("            return component == null");
        sb.AppendLine("                ? (float)DefaultComponent." + Identifier(input.Property.Name));
        sb.AppendLine("                : (float)component." + Identifier(input.Property.Name) + ";");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        static float Normalize(float value)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (float.IsNaN(value) || float.IsInfinity(value))");
        sb.AppendLine("                value = (float)DefaultComponent." + Identifier(input.Property.Name) + ";");
        sb.AppendLine("            if (value < " + FloatLiteral(input.Minimum) + ") value = " + FloatLiteral(input.Minimum) + ";");
        sb.AppendLine("            if (value > " + FloatLiteral(input.Maximum) + ") value = " + FloatLiteral(input.Maximum) + ";");
        if (input.Quantum > 0f)
        {
            sb.AppendLine("            value = (float)global::System.Math.Round(value / " + FloatLiteral(input.Quantum) + ") * " + FloatLiteral(input.Quantum) + ";");
            sb.AppendLine("            if (value < " + FloatLiteral(input.Minimum) + ") value = " + FloatLiteral(input.Minimum) + ";");
            sb.AppendLine("            if (value > " + FloatLiteral(input.Maximum) + ") value = " + FloatLiteral(input.Maximum) + ";");
        }
        sb.AppendLine("            return value;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildSwitch(SettingModel model)
    {
        var input = model.Input;
        var componentType = TypeName(model.ComponentType);
        var onText = string.IsNullOrEmpty(input.OnText) ? "HudInfoOn" : input.OnText;
        var offText = string.IsNullOrEmpty(input.OffText) ? "HudInfoOff" : input.OffText;
        var hasMinimumHeightToWidthRatio = input.MinimumHeightToWidthRatio > 0f;
        var sb = Header();
        sb.AppendLine("namespace LcdMod.Client.Terminal.Controls.GeneratedSettings");
        sb.AppendLine("{");
        sb.AppendLine("    internal sealed class " + model.ClassName + " : global::LcdMod.Client.Terminal.Controls.TerminalControlsWrapper");
        sb.AppendLine("    {");
        sb.AppendLine("        static readonly " + componentType + " DefaultComponent = new " + componentType + "();");
        sb.AppendLine();
        sb.AppendLine("        public override global::Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl TerminalControl { get; }");
        if (input.RequiresAdvancedTweakables)
            sb.AppendLine("        protected override bool RequiresAdvancedTweakables => true;");
        sb.AppendLine();
        sb.AppendLine("        public " + model.ClassName + "()");
        sb.AppendLine("        {");
        sb.AppendLine("            var control = CreateControl<global::Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlOnOffSwitch>(" + Literal(input.ControlId) + ");");
        sb.AppendLine("            control.Getter = Getter;");
        sb.AppendLine("            control.Setter = Setter;");
        sb.AppendLine("            control.Visible = Visible;");
        if (hasMinimumHeightToWidthRatio)
            sb.AppendLine("            control.Enabled = Enabled;");
        AppendControlTitle(sb, input, "control");
        if (!string.IsNullOrEmpty(input.Tooltip))
            sb.AppendLine("            control.Tooltip = global::VRage.Utils.MyStringId.GetOrCompute(" + Literal(input.Tooltip) + ");");
        sb.AppendLine("            control.OnText = global::VRage.Utils.MyStringId.GetOrCompute(" + Literal(onText) + ");");
        sb.AppendLine("            control.OffText = global::VRage.Utils.MyStringId.GetOrCompute(" + Literal(offText) + ");");
        sb.AppendLine("            TerminalControl = control;");
        sb.AppendLine("        }");
        sb.AppendLine();
        AppendVisibility(sb, model);
        sb.AppendLine();
        sb.AppendLine("        void Setter(global::Sandbox.ModAPI.IMyTerminalBlock block, bool value)");
        sb.AppendLine("        {");
        if (hasMinimumHeightToWidthRatio)
        {
            sb.AppendLine("            if (!MeetsMinimumHeightToWidthRatio(block))");
            sb.AppendLine("                value = false;");
        }
        if (input.RefreshTerminalOnSet)
        {
            sb.AppendLine("            if (global::LcdMod.Client.Config.ConfigManager.ModifyComponentForCurrentSurface<" + componentType + ">(");
            sb.AppendLine("                    block,");
            sb.AppendLine("                    " + Literal(model.Slot) + ",");
            sb.AppendLine("                    component => component." + Identifier(input.Property.Name) + " = value))");
            sb.AppendLine("                global::LcdMod.Client.Extensions.IMyTerminalBlockExtensions.RefreshTerminal(block);");
        }
        else
        {
            sb.AppendLine("            global::LcdMod.Client.Config.ConfigManager.ModifyComponentForCurrentSurface<" + componentType + ">(");
            sb.AppendLine("                block,");
            sb.AppendLine("                " + Literal(model.Slot) + ",");
            sb.AppendLine("                component => component." + Identifier(input.Property.Name) + " = value);");
        }
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        bool Getter(global::Sandbox.ModAPI.IMyTerminalBlock block)");
        sb.AppendLine("        {");
        if (hasMinimumHeightToWidthRatio)
        {
            sb.AppendLine("            if (!MeetsMinimumHeightToWidthRatio(block))");
            sb.AppendLine("                return false;");
        }
        sb.AppendLine("            var component = global::LcdMod.Client.Config.ConfigManager.GetComponentForCurrentSurface<" + componentType + ">(");
        sb.AppendLine("                block,");
        sb.AppendLine("                " + Literal(model.Slot) + ");");
        sb.AppendLine("            return component == null");
        sb.AppendLine("                ? DefaultComponent." + Identifier(input.Property.Name));
        sb.AppendLine("                : component." + Identifier(input.Property.Name) + ";");
        sb.AppendLine("        }");
        if (hasMinimumHeightToWidthRatio)
        {
            sb.AppendLine();
            sb.AppendLine("        bool Enabled(global::Sandbox.ModAPI.IMyTerminalBlock block)");
            sb.AppendLine("        {");
            sb.AppendLine("            return MeetsMinimumHeightToWidthRatio(block);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        static bool MeetsMinimumHeightToWidthRatio(global::Sandbox.ModAPI.IMyTerminalBlock block)");
            sb.AppendLine("        {");
            sb.AppendLine("            return global::LcdMod.Client.Helpers.SurfaceAspectRatioHelper.MeetsMinimumHeightToWidthRatio(");
            sb.AppendLine("                block,");
            sb.AppendLine("                " + FloatLiteral(input.MinimumHeightToWidthRatio) + ");");
            sb.AppendLine("        }");
        }
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string BuildColor(SettingModel model)
    {
        var input = model.Input;
        var componentType = TypeName(model.ComponentType);
        var sb = Header();
        sb.AppendLine("namespace LcdMod.Client.Terminal.Controls.GeneratedSettings");
        sb.AppendLine("{");
        sb.AppendLine("    internal sealed class " + model.ClassName + " : global::LcdMod.Client.Terminal.Controls.TerminalControlsWrapper");
        sb.AppendLine("    {");
        if (!model.OptionalColor)
            sb.AppendLine("        static readonly " + componentType + " DefaultComponent = new " + componentType + "();");
        sb.AppendLine();
        sb.AppendLine("        public override global::Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControl TerminalControl { get; }");
        if (input.RequiresAdvancedTweakables)
            sb.AppendLine("        protected override bool RequiresAdvancedTweakables => true;");
        sb.AppendLine();
        sb.AppendLine("        public " + model.ClassName + "()");
        sb.AppendLine("        {");
        sb.AppendLine("            var control = CreateControl<global::Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlColor>(" + Literal(input.ControlId) + ");");
        sb.AppendLine("            control.Getter = Getter;");
        sb.AppendLine("            control.Setter = Setter;");
        sb.AppendLine("            control.Visible = Visible;");
        AppendControlTitle(sb, input, "control");
        if (!string.IsNullOrEmpty(input.Tooltip))
            sb.AppendLine("            control.Tooltip = global::VRage.Utils.MyStringId.GetOrCompute(" + Literal(input.Tooltip) + ");");
        sb.AppendLine("            TerminalControl = control;");
        sb.AppendLine("        }");
        sb.AppendLine();
        AppendVisibility(sb, model);
        sb.AppendLine();
        sb.AppendLine("        void Setter(global::Sandbox.ModAPI.IMyTerminalBlock block, global::VRageMath.Color value)");
        sb.AppendLine("        {");
        sb.AppendLine("            global::LcdMod.Client.Config.ConfigManager.ModifyComponentForCurrentSurface<" + componentType + ">(");
        sb.AppendLine("                block,");
        sb.AppendLine("                " + Literal(model.Slot) + ",");
        if (model.OptionalColor)
            sb.AppendLine("                component => component." + Identifier(input.Property.Name) + ".Set(value));");
        else
            sb.AppendLine("                component => component." + Identifier(input.Property.Name) + " = value);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        global::VRageMath.Color Getter(global::Sandbox.ModAPI.IMyTerminalBlock block)");
        sb.AppendLine("        {");
        sb.AppendLine("            var component = global::LcdMod.Client.Config.ConfigManager.GetComponentForCurrentSurface<" + componentType + ">(");
        sb.AppendLine("                block,");
        sb.AppendLine("                " + Literal(model.Slot) + ");");
        if (model.OptionalColor)
        {
            var resolverType = TypeName(model.ColorResolver.ContainingType);
            var resolverName = Identifier(model.ColorResolver.Name);
            if (model.ColorResolver.Parameters.Length == 2)
                sb.AppendLine("            return " + resolverType + "." + resolverName + "(component, block);");
            else
                sb.AppendLine("            return " + resolverType + "." + resolverName + "(component);");
        }
        else
        {
            sb.AppendLine("            return component == null");
            sb.AppendLine("                ? DefaultComponent." + Identifier(input.Property.Name));
            sb.AppendLine("                : component." + Identifier(input.Property.Name) + ";");
        }
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static void AppendControlTitle(StringBuilder sb, SettingInput input, string controlName)
    {
        if (string.IsNullOrEmpty(input.TitleSuffix))
        {
            sb.AppendLine("            " + controlName + ".Title = global::VRage.Utils.MyStringId.GetOrCompute(" + Literal(input.Title ?? string.Empty) + ");");
            return;
        }

        sb.AppendLine("            " + controlName + ".Title = global::VRage.Utils.MyStringId.GetOrCompute(");
        sb.AppendLine("                global::VRage.MyTexts.Get(global::VRage.Utils.MyStringId.GetOrCompute(" + Literal(input.Title ?? string.Empty) + ")).ToString() + \" \" +");
        sb.AppendLine("                global::VRage.MyTexts.Get(global::VRage.Utils.MyStringId.GetOrCompute(" + Literal(input.TitleSuffix) + ")).ToString());");
    }

    static bool TryGetColorShape(
        ITypeSymbol propertyType,
        INamedTypeSymbol colorType,
        INamedTypeSymbol optionalValueType,
        out bool optional)
    {
        if (SymbolEqualityComparer.Default.Equals(propertyType, colorType))
        {
            optional = false;
            return true;
        }

        var named = propertyType as INamedTypeSymbol;
        if (named != null
            && named.TypeArguments.Length == 1
            && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, optionalValueType)
            && SymbolEqualityComparer.Default.Equals(named.TypeArguments[0], colorType))
        {
            optional = true;
            return true;
        }

        optional = false;
        return false;
    }

    static IEnumerable<IMethodSymbol> FindColorResolvers(
        INamedTypeSymbol resolverType,
        string resolverName,
        INamedTypeSymbol componentType,
        INamedTypeSymbol colorType,
        INamedTypeSymbol terminalBlockType)
    {
        return resolverType.GetMembers(resolverName)
            .OfType<IMethodSymbol>()
            .Where(method =>
                method.IsStatic
                && method.DeclaredAccessibility == Accessibility.Public
                && SymbolEqualityComparer.Default.Equals(method.ReturnType, colorType)
                && (method.Parameters.Length == 1 || method.Parameters.Length == 2)
                && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, componentType)
                && (method.Parameters.Length == 1
                    || SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, terminalBlockType)));
    }

    static bool HasCustomColorFlag(INamedTypeSymbol componentType)
    {
        for (var current = componentType; current != null; current = current.BaseType)
        {
            var property = current.GetMembers("CustomizedColors")
                .OfType<IPropertySymbol>()
                .FirstOrDefault();
            if (property != null
                && property.DeclaredAccessibility == Accessibility.Public
                && property.GetMethod != null
                && property.Type.SpecialType == SpecialType.System_Boolean)
                return true;
        }
        return false;
    }

    static void AppendVisibility(StringBuilder sb, SettingModel model)
    {
        var componentType = TypeName(model.ComponentType);
        sb.AppendLine("        public override bool VisibleForScript(string script)");
        sb.AppendLine("        {");
        sb.AppendLine("            return true;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        protected override bool IsAvailableForCurrentConfig(global::Sandbox.ModAPI.IMyTerminalBlock block)");
        sb.AppendLine("        {");
        if (!string.IsNullOrWhiteSpace(model.Input.OnlyApps))
        {
            sb.AppendLine("            var appSurface = global::LcdMod.Client.Config.ConfigManager.GetSurfaceConfigForCurrentScreen(block);");
            sb.AppendLine("            global::Generated.AppType appType;");
            sb.AppendLine("            if (appSurface == null || !global::Generated.AppSchemaRegistry.TryNormalizeAppType(appSurface.AppTypeId, out appType))");
            sb.AppendLine("                return false;");
            var apps = model.Input.OnlyApps.Split(',').Select(a => "appType != global::Generated.AppType." + a.Trim());
            sb.AppendLine("            if (" + string.Join(" && ", apps) + ")");
            sb.AppendLine("                return false;");
        }
        if (model.Input.RequiresCustomColor)
        {
            sb.AppendLine("            var component = global::LcdMod.Client.Config.ConfigManager.GetComponentForCurrentSurface<" + componentType + ">(");
            sb.AppendLine("                block,");
            sb.AppendLine("                " + Literal(model.Slot) + ");");
            sb.AppendLine("            return component != null && component.CustomizedColors;");
        }
        else
        {
            sb.AppendLine("            return global::LcdMod.Client.Config.ConfigManager.GetComponentForCurrentSurface<" + componentType + ">(");
            sb.AppendLine("                block,");
            sb.AppendLine("                " + Literal(model.Slot) + ") != null;");
        }
        sb.AppendLine("        }");
    }

    static string BuildRegistry(List<SettingModel> settings)
    {
        var sb = Header();
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using LcdMod.Client.Terminal;");
        sb.AppendLine();
        sb.AppendLine("namespace Generated");
        sb.AppendLine("{");
        sb.AppendLine("    public static class GeneratedTerminalControlRegistry");
        sb.AppendLine("    {");
        sb.AppendLine("        public static void AddTo(List<TerminalControlRegistration> registrations)");
        sb.AppendLine("        {");
        foreach (var setting in settings.OrderBy(item => item.Input.RegistrationId).ThenBy(item => item.Input.ControlId, StringComparer.Ordinal))
        {
            sb.AppendLine("            registrations.Add(new TerminalControlRegistration(");
            sb.AppendLine("                " + setting.Input.RegistrationId + ",");
            sb.AppendLine("                new global::LcdMod.Client.Terminal.Controls.GeneratedSettings." + setting.ClassName + "()));");
        }
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string ConvertFromFloat(string value, ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Single: return value;
            case SpecialType.System_Double: return "(double)" + value;
            case SpecialType.System_Int32: return "(int)global::System.Math.Round(" + value + ")";
            case SpecialType.System_Int16: return "(short)global::System.Math.Round(" + value + ")";
            case SpecialType.System_UInt16: return "(ushort)global::System.Math.Round(" + value + ")";
            case SpecialType.System_SByte: return "(sbyte)global::System.Math.Round(" + value + ")";
            case SpecialType.System_Byte: return "(byte)global::System.Math.Round(" + value + ")";
            default: return "(" + TypeName(type) + ")" + value;
        }
    }

    static string Display(SettingModel model) => model.ComponentType.ToDisplayString() + "." + model.Input.Property.Name;

    static string GeneratedClassName(SettingInput input)
    {
        string prefix;
        switch (input.Kind)
        {
            case SettingKind.Slider:
                prefix = "GeneratedSlider_";
                break;
            case SettingKind.Switch:
                prefix = "GeneratedSwitch_";
                break;
            default:
                prefix = "GeneratedColor_";
                break;
        }
        return prefix + SafeName(input.Property.ContainingType.ToDisplayString()) + "_" + SafeName(input.Property.Name);
    }

    static string SafeName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        return builder.ToString();
    }

    static StringBuilder Header()
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        return sb;
    }

    static string TypeName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    static string Identifier(string value) => SyntaxFacts.GetKeywordKind(value) == SyntaxKind.None ? value : "@" + value;
    static string Literal(string value) => SymbolDisplay.FormatLiteral(value ?? string.Empty, true);
    static string FloatLiteral(float value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "f";

    static Location AttributeLocation(AttributeData attribute, ISymbol fallback)
    {
        return attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
               ?? fallback.Locations.FirstOrDefault()
               ?? Location.None;
    }

    const string ATTRIBUTE_CONFIG_TYPE_SOURCE = """
// <auto-generated/>
namespace Generated
{
    internal interface IAttributeConfigType<TConfig>
    {
    }

    internal sealed class TerminalSlider
    {
    }

    internal sealed class TerminalSwitch
    {
    }

    internal sealed class TerminalColor
    {
    }

    [global::System.AttributeUsage(global::System.AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    internal sealed class TerminalControlSliderAttribute : global::System.Attribute, IAttributeConfigType<TerminalSlider>
    {
        public TerminalControlSliderAttribute(
            int registrationId,
            string controlId,
            string title,
            float minimum,
            float maximum,
            string writerFormat)
        {
            RegistrationId = registrationId;
            ControlId = controlId;
            Title = title;
            Minimum = minimum;
            Maximum = maximum;
            WriterFormat = writerFormat;
        }

        public int RegistrationId { get; private set; }
        public string ControlId { get; private set; }
        public string Title { get; private set; }
        public float Minimum { get; private set; }
        public float Maximum { get; private set; }
        public string WriterFormat { get; private set; }

        public string Tooltip { get; set; }
        public string Slot { get; set; }
        public string WriterSuffix { get; set; }
        public bool RequiresAdvancedTweakables { get; set; }
        public string OnlyApps { get; set; }
        public float Quantum { get; set; }
    }

    [global::System.AttributeUsage(global::System.AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    internal sealed class TerminalControlSwitchAttribute : global::System.Attribute, IAttributeConfigType<TerminalSwitch>
    {
        public TerminalControlSwitchAttribute(
            int registrationId,
            string controlId,
            string title)
        {
            RegistrationId = registrationId;
            ControlId = controlId;
            Title = title;
        }

        public int RegistrationId { get; private set; }
        public string ControlId { get; private set; }
        public string Title { get; private set; }

        public string TitleSuffix { get; set; }
        public string Tooltip { get; set; }
        public string Slot { get; set; }
        public string OnText { get; set; }
        public string OffText { get; set; }
        public bool RequiresAdvancedTweakables { get; set; }
        public string OnlyApps { get; set; }
        public float MinimumHeightToWidthRatio { get; set; }
        public bool RefreshTerminalOnSet { get; set; }
    }

    [global::System.AttributeUsage(global::System.AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    internal sealed class TerminalControlColorAttribute : global::System.Attribute, IAttributeConfigType<TerminalColor>
    {
        public TerminalControlColorAttribute(
            int registrationId,
            string controlId,
            string title)
        {
            RegistrationId = registrationId;
            ControlId = controlId;
            Title = title;
        }

        public int RegistrationId { get; private set; }
        public string ControlId { get; private set; }
        public string Title { get; private set; }

        public string Tooltip { get; set; }
        public string Slot { get; set; }
        public bool RequiresCustomColor { get; set; }
        public bool RequiresAdvancedTweakables { get; set; }
        public string OnlyApps { get; set; }
    }
}
""";

    static int GetInt(AttributeData attribute, int index)
    {
        if (attribute.ConstructorArguments.Length <= index || attribute.ConstructorArguments[index].Value == null)
            return 0;
        return Convert.ToInt32(attribute.ConstructorArguments[index].Value);
    }

    static float GetFloat(AttributeData attribute, int index)
    {
        if (attribute.ConstructorArguments.Length <= index || attribute.ConstructorArguments[index].Value == null)
            return 0f;
        return Convert.ToSingle(attribute.ConstructorArguments[index].Value);
    }

    static string GetString(AttributeData attribute, int index)
    {
        return attribute.ConstructorArguments.Length <= index
            ? null
            : attribute.ConstructorArguments[index].Value as string;
    }

    static string GetNamedString(AttributeData attribute, string name)
    {
        foreach (var pair in attribute.NamedArguments)
            if (pair.Key == name)
                return pair.Value.Value as string;
        return null;
    }

    static bool GetNamedBool(AttributeData attribute, string name)
    {
        foreach (var pair in attribute.NamedArguments)
            if (pair.Key == name && pair.Value.Value != null)
                return (bool)pair.Value.Value;
        return false;
    }

    static float GetNamedFloat(AttributeData attribute, string name)
    {
        foreach (var pair in attribute.NamedArguments)
            if (pair.Key == name && pair.Value.Value != null)
                return Convert.ToSingle(pair.Value.Value);
        return 0f;
    }

    enum SettingKind
    {
        Slider,
        Switch,
        Color
    }

    sealed class SettingInput
    {
        public SettingInput(
            SettingKind kind,
            IPropertySymbol property,
            int registrationId,
            string controlId,
            string title,
            float minimum,
            float maximum,
            string writerFormat,
            string tooltip,
            string slot,
            string writerSuffix,
            bool requiresAdvancedTweakables,
            float quantum,
            string onText,
            string offText,
            string titleSuffix,
            float minimumHeightToWidthRatio,
            bool refreshTerminalOnSet,
            bool requiresCustomColor,
            string onlyApps,
            Location location)
        {
            OnlyApps = onlyApps;
            Kind = kind;
            Property = property;
            RegistrationId = registrationId;
            ControlId = controlId;
            Title = title;
            Minimum = minimum;
            Maximum = maximum;
            WriterFormat = writerFormat;
            Tooltip = tooltip;
            Slot = slot;
            WriterSuffix = writerSuffix;
            RequiresAdvancedTweakables = requiresAdvancedTweakables;
            Quantum = quantum;
            OnText = onText;
            OffText = offText;
            TitleSuffix = titleSuffix;
            MinimumHeightToWidthRatio = minimumHeightToWidthRatio;
            RefreshTerminalOnSet = refreshTerminalOnSet;
            RequiresCustomColor = requiresCustomColor;
            Location = location;
        }

        public SettingKind Kind { get; }
        public IPropertySymbol Property { get; }
        public int RegistrationId { get; }
        public string ControlId { get; }
        public string Title { get; }
        public float Minimum { get; }
        public float Maximum { get; }
        public string WriterFormat { get; }
        public string Tooltip { get; }
        public string Slot { get; }
        public string WriterSuffix { get; }
        public bool RequiresAdvancedTweakables { get; }
        public float Quantum { get; }
        public string OnText { get; }
        public string OffText { get; }
        public string TitleSuffix { get; }
        public float MinimumHeightToWidthRatio { get; }
        public bool RefreshTerminalOnSet { get; }
        public bool RequiresCustomColor { get; }
        // AppType names, comma separated: the control shows only on those apps' screens.
        public string OnlyApps { get; }
        public Location Location { get; }
    }

    sealed class SettingModel
    {
        public SettingModel(
            SettingInput input,
            INamedTypeSymbol componentType,
            string slot,
            string className,
            bool optionalColor,
            IMethodSymbol colorResolver)
        {
            Input = input;
            ComponentType = componentType;
            Slot = slot;
            ClassName = className;
            OptionalColor = optionalColor;
            ColorResolver = colorResolver;
        }

        public SettingInput Input { get; }
        public INamedTypeSymbol ComponentType { get; }
        public string Slot { get; }
        public string ClassName { get; }
        public bool OptionalColor { get; }
        public IMethodSymbol ColorResolver { get; }
    }

    sealed class ComponentSlotTarget
    {
        public ComponentSlotTarget(ImmutableArray<ComponentSlotInput> slots)
        {
            Slots = slots;
        }

        public ImmutableArray<ComponentSlotInput> Slots { get; }
    }

    sealed class ComponentSlotInput
    {
        public ComponentSlotInput(INamedTypeSymbol componentType, string slot)
        {
            ComponentType = componentType;
            Slot = slot;
        }

        public INamedTypeSymbol ComponentType { get; }
        public string Slot { get; }
    }
}
