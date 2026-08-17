using LcdMod.Common.Config.Components;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class ItemDisplayConfigComponentTests
{
    [Theory]
    [InlineData(0, false, ItemDisplayMode.Card)]
    [InlineData(1, false, ItemDisplayMode.List)]
    [InlineData(1, true, ItemDisplayMode.Table)]
    [InlineData(0, true, ItemDisplayMode.Grid)]
    public void LegacyDisplayCombination_MigratesToExpectedMode(
        int legacyDisplayMode,
        bool drawLines,
        ItemDisplayMode expected)
    {
        var legacy = new GeneralConfigComponent
        {
            DisplayMode = legacyDisplayMode,
            DrawLines = drawLines
        };
        var config = new ItemDisplayConfigComponent();

        Assert.True(config.MigrateLegacyDisplayMode(legacy));
        Assert.Equal(expected, config.ResolveDisplayMode(legacy));
    }

    [Fact]
    public void ExplicitDisplayMode_IsNotOverwrittenByLegacyMigration()
    {
        var config = new ItemDisplayConfigComponent
        {
            DisplayMode = (int)ItemDisplayMode.List
        };
        var legacy = new GeneralConfigComponent
        {
            DisplayMode = 0,
            DrawLines = true
        };

        Assert.False(config.MigrateLegacyDisplayMode(legacy));
        Assert.Equal(ItemDisplayMode.List, config.ResolveDisplayMode(legacy));
    }
}
