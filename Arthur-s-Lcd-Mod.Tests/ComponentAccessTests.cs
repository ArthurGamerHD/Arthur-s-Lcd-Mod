using Generated;
using LcdMod.Common.Config.Components;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class ComponentAccessTests
{
    [Fact]
    public void GetComponent_ByType_ReturnsUniqueSchemaComponent()
    {
        var surface = AppSchemaRegistry.CreateSurface(AppType.Power, 0);

        var power = surface.GetComponent<PowerConfigComponent>();

        Assert.Same(surface.Get<PowerConfigComponent>(LcdMod.Common.Helpers.Constants.APP), power);
    }

    [Fact]
    public void GetComponent_ByType_RejectsAmbiguousSemanticReferences()
    {
        var surface = AppSchemaRegistry.CreateSurface(AppType.Projector, 0);
        surface.Components.Add(new ConfigComponentEntry(
            LcdMod.Common.Helpers.Constants.VISIBLE_TREE_REFERENCE,
            new BlockReferenceConfigComponent()));

        Assert.Throws<InvalidOperationException>(() => surface.GetComponent<BlockReferenceConfigComponent>());
        Assert.Same(
            surface.Get<BlockReferenceConfigComponent>(LcdMod.Common.Helpers.Constants.PROJECTOR_REFERENCE),
            surface.GetComponent<BlockReferenceConfigComponent>(LcdMod.Common.Helpers.Constants.PROJECTOR_REFERENCE));
    }

    [Fact]
    public void MissingRequiredComponent_FailsWithoutMutatingGraph()
    {
        var surface = AppSchemaRegistry.CreateSurface(AppType.Farm, 0);
        var count = surface.Components.Count;

        Assert.Throws<InvalidOperationException>(() => surface.GetComponent<PowerConfigComponent>());
        Assert.Equal(count, surface.Components.Count);
    }
}
