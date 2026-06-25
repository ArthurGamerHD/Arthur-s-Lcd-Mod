using Generated;
using LcdMod.Client.Terminal.Controls.Generic;

namespace LcdMod.Client.Terminal.Controls.Groups
{
    public abstract class BaseTerminalControlGroup : ITerminalControlGroup,
        IContainsTerminalControl<TextSurfaceControlsVisibility>,
        IContainsTerminalControl<Scale.SliderScale>,
        IContainsTerminalControl<SliderBrightness>

    {
    }
}
