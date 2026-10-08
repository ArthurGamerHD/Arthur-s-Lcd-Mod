using LcdMod.Common.Config.Components;
using ManagedDoom.SE;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class DoomInputSettingsTests
{
    [Fact]
    public void MouseTurningDefaultsOnAndCanBeExplicitlyDisabled()
    {
        var config = new GeneralConfigComponent();

        Assert.True(DoomInputSettings.GetMouseTurningEnabled(config));

        DoomInputSettings.SetMouseTurningEnabled(config, false);
        Assert.False(DoomInputSettings.GetMouseTurningEnabled(config));

        DoomInputSettings.SetMouseTurningEnabled(config, true);
        Assert.True(DoomInputSettings.GetMouseTurningEnabled(config));
    }
}
