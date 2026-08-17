using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Terminal.Controls;

namespace LcdMod.Client.Apps.ViewModel
{
    public sealed class ItemSortHeaderModel : ButtonModel
    {
        public SortMethod Column { get; set; }
    }
}
