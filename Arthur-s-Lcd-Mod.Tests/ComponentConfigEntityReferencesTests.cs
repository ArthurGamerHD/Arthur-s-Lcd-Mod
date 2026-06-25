using LcdMod.Common.Config.Components;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class ComponentConfigEntityReferencesTests
{
    [Fact]
    public void RemapEntityReferences_UpdatesBlocksReferencesAndNestedApps()
    {
        var surface = new SurfaceConfig { SurfaceIndex = 0, AppTypeId = 1 };
        surface.Set(LcdMod.Common.Helpers.Constants.BLOCKS, new BlockSelectionConfigComponent
        {
            SelectedBlocks = new long[] { 10, 20, 20, 0 }
        });
        surface.Set(LcdMod.Common.Helpers.Constants.PROJECTOR_REFERENCE, new BlockReferenceConfigComponent { EntityId = 30 });

        var nested = new AppInstanceConfig { InstanceId = 1, AppKind = 16 };
        nested.Set(LcdMod.Common.Helpers.Constants.RENDER_PROXY_REFERENCE, new BlockReferenceConfigComponent { EntityId = 40 });
        surface.Set(LcdMod.Common.Helpers.Constants.TABS, new TabContainerConfigComponent
        {
            Apps = new List<AppInstanceConfig> { nested }
        });

        var changed = ComponentConfigEntityReferences.RemapEntityReferences(
            surface,
            new Dictionary<long, long>
            {
                [10] = 100,
                [20] = 200,
                [30] = 300,
                [40] = 400
            });

        Assert.True(changed);
        Assert.Equal(new long[] { 100, 200 }, surface.Get<BlockSelectionConfigComponent>(LcdMod.Common.Helpers.Constants.BLOCKS).SelectedBlocks);
        Assert.Equal(300, surface.Get<BlockReferenceConfigComponent>(LcdMod.Common.Helpers.Constants.PROJECTOR_REFERENCE).EntityId);
        Assert.Equal(400, nested.Get<BlockReferenceConfigComponent>(LcdMod.Common.Helpers.Constants.RENDER_PROXY_REFERENCE).EntityId);
    }

    [Fact]
    public void CollectPinnedEntityIds_IncludesBlocksReferencesAndNestedApps()
    {
        var surface = new SurfaceConfig { SurfaceIndex = 0, AppTypeId = 1 };
        surface.Set(LcdMod.Common.Helpers.Constants.BLOCKS, new BlockSelectionConfigComponent
        {
            SelectedBlocks = new long[] { 10, 0, 20 }
        });
        surface.Set(LcdMod.Common.Helpers.Constants.DOCKABLE_REFERENCE, new BlockReferenceConfigComponent { EntityId = 30 });

        var nested = new AppInstanceConfig { InstanceId = 1, AppKind = 22 };
        nested.Set(LcdMod.Common.Helpers.Constants.VISIBLE_TREE_REFERENCE, new BlockReferenceConfigComponent { EntityId = 40 });
        surface.Set(LcdMod.Common.Helpers.Constants.TABS, new TabContainerConfigComponent
        {
            Apps = new List<AppInstanceConfig> { nested }
        });

        var entityIds = new List<long>();
        ComponentConfigEntityReferences.CollectPinnedEntityIds(surface, entityIds);

        Assert.Equal(new long[] { 10, 20, 30, 40 }, entityIds);
    }
}
