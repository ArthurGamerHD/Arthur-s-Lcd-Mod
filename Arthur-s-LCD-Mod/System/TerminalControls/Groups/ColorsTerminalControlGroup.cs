using Generated;
using Graph.System.TerminalControls.Color;

namespace Graph.System.TerminalControls.Groups
{
    public abstract class ColorsTerminalControlGroup : ITerminalControlGroup,
        IContainsTerminalControl<SwitchToggleColors>,
        IContainsTerminalControl<ColorPickerAccent>,
        IContainsTerminalControl<ColorPickerWarning>,
        IContainsTerminalControl<ColorPickerError>
    {
    }
}