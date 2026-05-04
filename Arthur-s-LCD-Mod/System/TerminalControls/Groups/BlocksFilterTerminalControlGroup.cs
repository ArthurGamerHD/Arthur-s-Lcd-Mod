using Generated;
using Graph.System.TerminalControls.Filter.Buttons;
using Graph.System.TerminalControls.Filter.Listbox;

namespace Graph.System.TerminalControls.Groups
{
    public abstract class BlocksFilterTerminalControlGroup : ITerminalControlGroup,
        IContainsTerminalControl<ListboxBlockCandidates>,
        IContainsTerminalControl<ButtonBlockAddToSelection>,
        IContainsTerminalControl<ListboxBlockSelected>,
        IContainsTerminalControl<ButtonBlockRemoveFromSelection>
    {
    }
}