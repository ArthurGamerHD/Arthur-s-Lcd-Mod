using Generated;
using LcdMod.Common.Config.Components;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class AppSchemaRegistryTests
{
    [Fact]
    public void CreateSurface_AddsExactProjectorSchema()
    {
        var surface = AppSchemaRegistry.CreateSurface(AppType.Projector, 3);

        Assert.Equal(3, surface.SurfaceIndex);
        Assert.Equal((int)AppType.Projector, surface.AppTypeId);
        Assert.NotNull(surface.Get<GeneralConfigComponent>(LcdMod.Common.Helpers.Constants.GENERAL));
        Assert.NotNull(surface.Get<ColorConfigComponent>(LcdMod.Common.Helpers.Constants.COLORS));
        Assert.NotNull(surface.Get<InteractiveConfigComponent>(LcdMod.Common.Helpers.Constants.INTERACTION));
        Assert.NotNull(surface.Get<FilterConfigComponent>(LcdMod.Common.Helpers.Constants.FILTERS));
        Assert.NotNull(surface.Get<BlockSelectionConfigComponent>(LcdMod.Common.Helpers.Constants.BLOCKS));
        Assert.NotNull(surface.Get<ItemSelectionConfigComponent>(LcdMod.Common.Helpers.Constants.ITEMS));
        Assert.NotNull(surface.Get<BlockReferenceConfigComponent>(LcdMod.Common.Helpers.Constants.PROJECTOR_REFERENCE));
        Assert.Equal(7, surface.Components.Count);
    }

    [Fact]
    public void FarmSchema_HasOnlyCommonHostComponents()
    {
        var surface = AppSchemaRegistry.CreateSurface(AppType.Farm, 0);

        Assert.NotNull(surface.Get<GeneralConfigComponent>(LcdMod.Common.Helpers.Constants.GENERAL));
        Assert.NotNull(surface.Get<ColorConfigComponent>(LcdMod.Common.Helpers.Constants.COLORS));
        Assert.NotNull(surface.Get<InteractiveConfigComponent>(LcdMod.Common.Helpers.Constants.INTERACTION));
        Assert.Null(surface.TryGet<PowerConfigComponent>(LcdMod.Common.Helpers.Constants.APP));
    }

    [Fact]
    public void ChangeApp_CopiesOnlyExactSlotAndTypeMatches()
    {
        var surface = AppSchemaRegistry.CreateSurface(AppType.Projector, 0);
        surface.Get<GeneralConfigComponent>(LcdMod.Common.Helpers.Constants.GENERAL).InternalScale = 2.5f;
        surface.Get<BlockSelectionConfigComponent>(LcdMod.Common.Helpers.Constants.BLOCKS).SelectedBlocks = new long[] { 10, 20 };
        surface.Set("extension.example", new ExtensionConfigComponent { Value = 42 });

        AppSchemaRegistry.ChangeApp(surface, AppType.Power);

        Assert.Equal((int)AppType.Power, surface.AppTypeId);
        Assert.Equal(2.5f, surface.Get<GeneralConfigComponent>(LcdMod.Common.Helpers.Constants.GENERAL).InternalScale);
        Assert.Null(surface.TryGet<BlockSelectionConfigComponent>(LcdMod.Common.Helpers.Constants.BLOCKS));
        Assert.Null(surface.TryGet<BlockReferenceConfigComponent>(LcdMod.Common.Helpers.Constants.PROJECTOR_REFERENCE));
        Assert.NotNull(surface.Get<PowerConfigComponent>(LcdMod.Common.Helpers.Constants.APP));
        Assert.Equal(42, surface.Get<ExtensionConfigComponent>("extension.example").Value);
    }

    [Fact]
    public void EnsureSchema_RepairsMissingWrongAndDuplicateRequiredSlots()
    {
        var surface = new SurfaceConfig
        {
            SurfaceIndex = 0,
            AppTypeId = (int)AppType.Power
        };
        surface.Set(LcdMod.Common.Helpers.Constants.GENERAL, new ColorConfigComponent());
        surface.Components.Add(new ConfigComponentEntry(LcdMod.Common.Helpers.Constants.APP, new RadarConfigComponent()));
        surface.Components.Add(new ConfigComponentEntry(LcdMod.Common.Helpers.Constants.APP, new PowerConfigComponent { GraphWindowIndex = 5 }));

        Assert.True(AppSchemaRegistry.EnsureSchema(surface));

        Assert.Single(surface.Components, entry => entry.Slot == LcdMod.Common.Helpers.Constants.GENERAL);
        Assert.IsType<GeneralConfigComponent>(surface.Get<GeneralConfigComponent>(LcdMod.Common.Helpers.Constants.GENERAL));
        Assert.Single(surface.Components, entry => entry.Slot == LcdMod.Common.Helpers.Constants.APP);
        Assert.Equal(5, surface.Get<PowerConfigComponent>(LcdMod.Common.Helpers.Constants.APP).GraphWindowIndex);
    }

    [Fact]
    public void Registry_UsesExactComponentContracts()
    {
        Assert.True(AppSchemaRegistry.IsAllowedComponent(AppType.Power, LcdMod.Common.Helpers.Constants.APP, typeof(PowerConfigComponent)));
        Assert.False(AppSchemaRegistry.IsAllowedComponent(AppType.Power, LcdMod.Common.Helpers.Constants.APP, typeof(RadarConfigComponent)));
        Assert.False(AppSchemaRegistry.IsAllowedComponent(AppType.Farm, LcdMod.Common.Helpers.Constants.APP, typeof(PowerConfigComponent)));
        Assert.True(AppSchemaRegistry.IsRegisteredSlot(LcdMod.Common.Helpers.Constants.PROJECTOR_REFERENCE));
        Assert.False(AppSchemaRegistry.IsRegisteredSlot("extension.example"));
    }

    [Fact]
    public void ExplicitUnknownAppType_DoesNotMutateKnownGraph()
    {
        var surface = AppSchemaRegistry.CreateSurface(AppType.Farm, 0);
        var general = surface.Get<GeneralConfigComponent>(LcdMod.Common.Helpers.Constants.GENERAL);
        var before = surface.Components.ToArray();

        var supported = AppSchemaRegistry.EnsureSchema(surface, (AppType)9001);

        Assert.False(supported);
        Assert.Equal((int)AppType.Farm, surface.AppTypeId);
        Assert.Equal(before, surface.Components.ToArray());
        Assert.Same(general, surface.Get<GeneralConfigComponent>(LcdMod.Common.Helpers.Constants.GENERAL));
    }

    [Fact]
    public void UnknownAppIdentity_IsNotNormalizedOrMutated()
    {
        var surface = new SurfaceConfig { SurfaceIndex = 0, AppTypeId = 9001 };
        var extension = new ExtensionConfigComponent { Value = 11 };
        surface.Set("extension.future", extension);

        var supported = AppSchemaRegistry.EnsureSchema(surface);

        Assert.False(supported);
        Assert.Equal(9001, surface.AppTypeId);
        Assert.Same(extension, surface.Get<ExtensionConfigComponent>("extension.future"));
    }

    sealed class ExtensionConfigComponent : ConfigComponent
    {
        public int Value { get; set; }
        public override ConfigComponent Clone() => new ExtensionConfigComponent { Value = Value };
    }
}

public sealed class DeferredNestedConfigTests
{
    [Fact]
    public void NormalizeAppInstanceIds_DoesNotInterpretNestedLegacyIdentity()
    {
        var nested = new AppInstanceConfig { InstanceId = 0, AppKind = 9002, Components = null };
        var tabs = new TabContainerConfigComponent
        {
            ActiveAppInstanceId = 99,
            NextAppInstanceId = 10,
            Apps = new List<AppInstanceConfig> { nested }
        };

        tabs.NormalizeAppInstanceIds();

        Assert.Equal(10UL, nested.InstanceId);
        Assert.Equal(9002, nested.AppKind);
        Assert.Null(nested.Components);
        Assert.Equal(nested.InstanceId, tabs.ActiveAppInstanceId);
    }
}
