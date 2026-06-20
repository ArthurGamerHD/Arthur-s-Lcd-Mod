using Generated;
using LcdMod.Client.Terminal.Controls.Generic;

namespace LcdMod.Client.Terminal.Controls.Groups
{
    public abstract class BaseTerminalControlGroup : ITerminalControlGroup,
        IContainsTerminalControlGroup<ColorsTerminalControlGroup>,
        IContainsTerminalControl<TextSurfaceControlsVisibility>,
        IContainsTerminalControl<SwitchToggleHeader>,
        IContainsTerminalControl<Scale.SliderScale>,
        IContainsTerminalControl<SliderBrightness>

    {
    }
}
