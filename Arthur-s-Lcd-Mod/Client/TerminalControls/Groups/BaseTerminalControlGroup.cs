using Generated;
using LcdMod.Client.TerminalControls.Generic;

namespace LcdMod.Client.TerminalControls.Groups
{
    public abstract class BaseTerminalControlGroup : ITerminalControlGroup,
        IContainsTerminalControlGroup<ColorsTerminalControlGroup>,
        IContainsTerminalControl<SwitchToggleHeader>,
        IContainsTerminalControl<Scale.SliderFontSize>,
        IContainsTerminalControl<SliderPadding>,
        IContainsTerminalControl<Scale.SliderScale>

    {
    }
}
