using Generated;
using LcdMod.Client.Terminal.Controls.Filter.Buttons;
using LcdMod.Client.Terminal.Controls.Filter.Listbox;
using LcdMod.Client.Terminal.Controls.Generic;

namespace LcdMod.Client.Terminal.Controls.Groups
{
    public abstract class BlocksFilterTerminalControlGroup : ITerminalControlGroup,
        IContainsTerminalControl<ComboboxLinkType>,
        IContainsTerminalControl<ListboxBlockCandidates>,
        IContainsTerminalControl<ButtonBlockAddToSelection>,
        IContainsTerminalControl<ListboxBlockSelected>,
        IContainsTerminalControl<ButtonBlockRemoveFromSelection>
    {
    }
}