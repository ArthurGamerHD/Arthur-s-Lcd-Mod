using Generated;
using LcdMod.Client.Terminal.Controls.Color;

namespace LcdMod.Client.Terminal.Controls.Groups
{
    public abstract class ColorsTerminalControlGroup : ITerminalControlGroup,
        IContainsTerminalControl<SwitchToggleColors>,
        IContainsTerminalControl<ColorPickerAccent>,
        IContainsTerminalControl<ColorPickerWarning>,
        IContainsTerminalControl<ColorPickerError>
    {
    }
}