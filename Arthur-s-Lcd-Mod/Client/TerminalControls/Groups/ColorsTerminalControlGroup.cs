using Generated;
using LcdMod.Client.TerminalControls.Color;

namespace LcdMod.Client.TerminalControls.Groups
{
    public abstract class ColorsTerminalControlGroup : ITerminalControlGroup,
        IContainsTerminalControl<SwitchToggleColors>,
        IContainsTerminalControl<ColorPickerAccent>,
        IContainsTerminalControl<ColorPickerWarning>,
        IContainsTerminalControl<ColorPickerError>
    {
    }
}