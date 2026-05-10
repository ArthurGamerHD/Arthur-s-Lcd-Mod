using Generated;
using LcdMod.Client.Terminal.Controls.Filter.Buttons;
using LcdMod.Client.Terminal.Controls.Filter.Listbox;

namespace LcdMod.Client.Terminal.Controls.Groups
{
    public abstract class ItemsFilterTerminalControlGroup : ITerminalControlGroup,
        IContainsTerminalControl<ListboxItemsCandidates>,
        IContainsTerminalControl<ButtonItemAddToSelection>,
        IContainsTerminalControl<ListboxItemsSelected>,
        IContainsTerminalControl<ButtonItemRemoveFromSelection>
    {
    }
}
