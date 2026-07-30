using Generated;
using LcdMod.Client.Terminal.Controls.Gps;

namespace LcdMod.Client.Terminal.Controls.Groups
{
    public abstract class GpsAlwaysDisplayTerminalControlGroup : ITerminalControlGroup,
        IContainsTerminalControl<ListboxGpsCandidates>,
        IContainsTerminalControl<ButtonGpsAddToAlwaysDisplay>,
        IContainsTerminalControl<ListboxGpsAlwaysDisplayed>,
        IContainsTerminalControl<ButtonGpsRemoveFromAlwaysDisplay>
    {
    }
}
