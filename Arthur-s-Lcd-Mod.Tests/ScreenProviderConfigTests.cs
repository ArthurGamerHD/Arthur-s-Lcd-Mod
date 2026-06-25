using Generated;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Models;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class ScreenProviderConfigTests
{
    [Fact]
    public void NormalizeComponentSchema_PreservesV0MigrationUntilConcreteAppBinds()
    {
        var unresolved = new SurfaceConfig
        {
            SurfaceIndex = 0,
            LegacyAppKind = 10,
            AppTypeId = 0
        };
        unresolved.Set(LcdMod.Common.Helpers.Constants.GENERAL, new GeneralConfigComponent { InternalScale = 2.25f });
        unresolved.Set(LcdMod.Common.Helpers.Constants.APP, new PowerConfigComponent { PowerHistoryTier = 8 });
        var provider = new ScreenProviderConfig
        {
            SchemaVersion = ScreenProviderConfig.COMPONENT_SCHEMA_VERSION,
            Surfaces = new List<SurfaceConfig> { unresolved }
        };

        Assert.True(provider.NormalizeComponentSchema());

        Assert.Equal(ScreenProviderConfig.COMPONENT_SCHEMA_VERSION, provider.SchemaVersion);
        Assert.Equal(0, unresolved.AppTypeId);
        Assert.Equal(10, unresolved.LegacyAppKind);
        Assert.Equal(8, unresolved.Get<PowerConfigComponent>(LcdMod.Common.Helpers.Constants.APP).PowerHistoryTier);
    }

    [Fact]
    public void EnsureSurfaceApp_PerformsBindTimeMigrationAndClearsLegacyIdentity()
    {
        var unresolved = new SurfaceConfig
        {
            SurfaceIndex = 0,
            LegacyAppKind = 10,
            AppTypeId = 0
        };
        unresolved.Set(LcdMod.Common.Helpers.Constants.GENERAL, new GeneralConfigComponent { InternalScale = 2.5f });
        unresolved.Set(LcdMod.Common.Helpers.Constants.APP, new PowerConfigComponent { GraphWindowIndex = 7 });
        var provider = CurrentProvider(unresolved);

        provider.EnsureSurfaceApp(0, AppType.Power);

        Assert.Equal((int)AppType.Power, unresolved.AppTypeId);
        Assert.Equal(0, unresolved.LegacyAppKind);
        Assert.Equal(2.5f, unresolved.Get<GeneralConfigComponent>(LcdMod.Common.Helpers.Constants.GENERAL).InternalScale);
        Assert.Equal(7, unresolved.Get<PowerConfigComponent>(LcdMod.Common.Helpers.Constants.APP).GraphWindowIndex);
    }

    [Fact]
    public void EnsureSurfaceApp_DoesNotReplaceUnknownFutureApp()
    {
        var unknown = new SurfaceConfig { SurfaceIndex = 0, AppTypeId = 8123 };
        unknown.Set("extension.future", new TestExtensionComponent { Value = 44 });
        var provider = CurrentProvider(unknown);

        provider.EnsureSurfaceApp(0, AppType.Farm);

        Assert.Same(unknown, provider.Surfaces[0]);
        Assert.Equal(8123, unknown.AppTypeId);
        Assert.Equal(44, unknown.Get<TestExtensionComponent>("extension.future").Value);
    }

    [Fact]
    public void NormalizeComponentSchema_TreatsNestedGraphAsOpaque()
    {
        var nested = new AppInstanceConfig { InstanceId = 0, AppKind = 9002, Components = null };
        var tabs = new TabContainerConfigComponent { Apps = new List<AppInstanceConfig> { nested } };
        var known = AppSchemaRegistry.CreateSurface(AppType.Farm, 0);
        known.Set(LcdMod.Common.Helpers.Constants.TABS, tabs);
        var provider = CurrentProvider(known);

        Assert.True(provider.NormalizeComponentSchema());

        Assert.NotEqual(0UL, nested.InstanceId);
        Assert.Null(nested.Components);
        Assert.Equal(9002, nested.AppKind);
    }

    [Fact]
    public void NormalizeComponentSchema_DoesNotInitializeUnknownFutureComponentGraph()
    {
        var unknown = new SurfaceConfig
        {
            SurfaceIndex = 0,
            AppTypeId = 8123,
            Components = null
        };
        var provider = CurrentProvider(unknown);

        Assert.True(provider.NormalizeComponentSchema());

        Assert.Equal(8123, unknown.AppTypeId);
        Assert.Null(unknown.Components);
    }

    [Fact]
    public void NormalizeComponentSchema_PreservesUnknownFutureSurfaceAsOpaque()
    {
        var nested = new AppInstanceConfig { InstanceId = 0, AppKind = 9002 };
        var tabs = new TabContainerConfigComponent
        {
            ActiveAppInstanceId = 99,
            NextAppInstanceId = 10,
            Apps = new List<AppInstanceConfig> { nested }
        };
        var unknown = new SurfaceConfig { SurfaceIndex = 0, AppTypeId = 8123 };
        unknown.Set(LcdMod.Common.Helpers.Constants.TABS, tabs);
        var provider = CurrentProvider(unknown);

        Assert.True(provider.NormalizeComponentSchema());

        Assert.Equal(8123, unknown.AppTypeId);
        Assert.Equal(0UL, nested.InstanceId);
        Assert.Equal(99UL, tabs.ActiveAppInstanceId);
        Assert.Same(tabs, unknown.Get<TabContainerConfigComponent>(LcdMod.Common.Helpers.Constants.TABS));
    }

    [Fact]
    public void CanWriteConfig_RejectsUnknownSurfaceWithoutLockingKnownSurfaces()
    {
        var known = AppSchemaRegistry.CreateSurface(AppType.Farm, 0);
        var unknown = new SurfaceConfig { SurfaceIndex = 1, AppTypeId = 8123 };
        var provider = new ScreenProviderConfig
        {
            SchemaVersion = ScreenProviderConfig.COMPONENT_SCHEMA_VERSION,
            Surfaces = new List<SurfaceConfig> { known, unknown }
        };

        Assert.True(provider.NormalizeComponentSchema());
        Assert.True(provider.CanWrite);
        Assert.True(provider.CanWriteConfig(known));
        Assert.False(provider.CanWriteConfig(unknown));
    }

    [Fact]
    public void NormalizeComponentSchema_LeavesNewerSchemaReadOnlyAndUntouched()
    {
        var surface = new SurfaceConfig { SurfaceIndex = 5, AppTypeId = 9123 };
        var extension = new TestExtensionComponent { Value = 77 };
        surface.Set("extension.future", extension);
        var provider = new ScreenProviderConfig
        {
            SchemaVersion = ScreenProviderConfig.COMPONENT_SCHEMA_VERSION + 1,
            Surfaces = new List<SurfaceConfig> { surface }
        };

        Assert.False(provider.NormalizeComponentSchema());

        Assert.True(provider.IsReadOnly);
        Assert.False(provider.CanWrite);
        Assert.Same(surface, provider.Surfaces[0]);
        Assert.Same(extension, provider.Surfaces[0].Get<TestExtensionComponent>("extension.future"));
    }

    static ScreenProviderConfig CurrentProvider(params SurfaceConfig[] surfaces)
    {
        return new ScreenProviderConfig
        {
            SchemaVersion = ScreenProviderConfig.COMPONENT_SCHEMA_VERSION,
            Surfaces = surfaces.ToList()
        };
    }

    sealed class TestExtensionComponent : ConfigComponent
    {
        public int Value { get; set; }
        public override ConfigComponent Clone() => new TestExtensionComponent { Value = Value };
    }
}
