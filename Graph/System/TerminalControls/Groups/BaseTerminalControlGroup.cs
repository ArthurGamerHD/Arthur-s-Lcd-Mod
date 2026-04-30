using Generated;
using Graph.System.TerminalControls.Generic;
using SliderFontSize = Graph.System.TerminalControls.Scale.SliderFontSize;
using SliderScale = Graph.System.TerminalControls.Scale.SliderScale;

namespace Graph.System.TerminalControls.Groups
{
    public abstract class BaseTerminalControlGroup : ITerminalControlGroup,
        IContainsTerminalControlGroup<ColorsTerminalControlGroup>,
        IContainsTerminalControl<SwitchToggleHeader>,
        IContainsTerminalControl<SliderFontSize>,
        IContainsTerminalControl<SliderPadding>,
        IContainsTerminalControl<SliderScale>

    {
    }
}
