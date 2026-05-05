using Generated;
using LcdMod.Client.TerminalControls.Filter.Buttons;
using LcdMod.Client.TerminalControls.Filter.Listbox;

namespace LcdMod.Client.TerminalControls.Groups
{
    public abstract class ItemsFilterTerminalControlGroup : ITerminalControlGroup,
        IContainsTerminalControl<ListboxItemsCandidates>,
        IContainsTerminalControl<ButtonItemAddToSelection>,
        IContainsTerminalControl<ListboxItemsSelected>,
        IContainsTerminalControl<ButtonItemRemoveFromSelection>
    {
    }
}
