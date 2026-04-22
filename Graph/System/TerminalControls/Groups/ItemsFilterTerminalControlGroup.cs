using Generated;
using Graph.System.TerminalControls.Filter.Buttons;
using Graph.System.TerminalControls.Filter.Listbox;

namespace Graph.System.TerminalControls.Groups
{
    public abstract class ItemsFilterTerminalControlGroup : ITerminalControlGroup,
        IContainsTerminalControl<ListboxItemsCandidates>,
        IContainsTerminalControl<ButtonItemAddToSelection>,
        IContainsTerminalControl<ListboxItemsSelected>,
        IContainsTerminalControl<ButtonItemRemoveFromSelection>
    {
    }
}
