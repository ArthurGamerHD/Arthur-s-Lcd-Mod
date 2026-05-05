using Generated;
using LcdMod.Client.TerminalControls.Filter.Buttons;
using LcdMod.Client.TerminalControls.Filter.Listbox;

namespace LcdMod.Client.TerminalControls.Groups
{
    public abstract class BlocksFilterTerminalControlGroup : ITerminalControlGroup,
        IContainsTerminalControl<ListboxBlockCandidates>,
        IContainsTerminalControl<ButtonBlockAddToSelection>,
        IContainsTerminalControl<ListboxBlockSelected>,
        IContainsTerminalControl<ButtonBlockRemoveFromSelection>
    {
    }
}