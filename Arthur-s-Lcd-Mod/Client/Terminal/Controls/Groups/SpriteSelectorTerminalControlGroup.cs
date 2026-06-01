using Generated;
using LcdMod.Client.Terminal.Controls.Filter.Buttons;
using LcdMod.Client.Terminal.Controls.Filter.Listbox;
using LcdMod.Client.Terminal.Controls.Generic;

namespace LcdMod.Client.Terminal.Controls.Groups
{
    public abstract class SpriteSelectorTerminalControlGroup : ITerminalControlGroup,
        IContainsTerminalControl<ListboxSpriteCandidates>,
        IContainsTerminalControl<ButtonSpriteAddToSelection>,
        IContainsTerminalControl<ListboxSpriteSelected>,
        IContainsTerminalControl<ButtonSpriteRemoveFromSelection>,
        IContainsTerminalControl<SliderImageChangeInterval>
    {
    }
}
