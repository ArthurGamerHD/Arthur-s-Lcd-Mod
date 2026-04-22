using Generated;
using Graph.System.TerminalControls.Generic;

namespace Graph.System.TerminalControls.Groups
{
    public abstract class BaseTerminalControlGroup : ITerminalControlGroup,
        IContainsTerminalControlGroup<ColorsTerminalControlGroup>,
        IContainsTerminalControl<SwitchToggleHeader>,
        IContainsTerminalControl<SliderScale>,
        IContainsTerminalControl<ComboboxDisplayMode>
    {
    }
}
